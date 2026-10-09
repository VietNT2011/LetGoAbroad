using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using EnglishHub.Application.Media;
using EnglishHub.Domain;
using Microsoft.Extensions.Configuration;
namespace EnglishHub.Infrastructure.Media;

/// <summary>VI: Browser chỉ ghi staging; canonical chỉ server ghi. EN: Browsers write staging only; verified canonical blobs never receive upload grants.</summary>
public sealed class AzureObjectStorage : IObjectStorage
{
    private readonly BlobServiceClient service;
    private readonly StorageSharedKeyCredential credential;
    private readonly string publicEndpoint;
    private readonly bool configureLocalCors;
    public AzureObjectStorage(IConfiguration config)
    {
        var connection = config["Storage:ConnectionString"] ?? throw new DomainError(503,"storage_unavailable","Chưa cấu hình blob storage.");
        service = new BlobServiceClient(connection);
        var settings = connection.Split(';',StringSplitOptions.RemoveEmptyEntries).Select(s => s.Split('=',2)).ToDictionary(x => x[0],x => x[1],StringComparer.OrdinalIgnoreCase);
        credential = new StorageSharedKeyCredential(settings["AccountName"],settings["AccountKey"]);
        publicEndpoint = (config["Storage:PublicEndpoint"] ?? service.Uri.ToString()).TrimEnd('/');
        configureLocalCors=config.GetValue<bool>("Storage:ConfigureLocalCors");
    }
    public async Task Ensure(CancellationToken ct)
    {
        await service.GetBlobContainerClient("staging").CreateIfNotExistsAsync(PublicAccessType.None,cancellationToken:ct);
        await service.GetBlobContainerClient("canonical").CreateIfNotExistsAsync(PublicAccessType.None,cancellationToken:ct);
        if (configureLocalCors)
        {
            var properties=(await service.GetPropertiesAsync(cancellationToken:ct)).Value;
            properties.Cors.Clear(); properties.Cors.Add(new BlobCorsRule { AllowedOrigins="http://127.0.0.1:5188,http://127.0.0.1:5177,http://127.0.0.1:5173,http://localhost:5173",AllowedMethods="GET,HEAD,PUT,OPTIONS",AllowedHeaders="*",ExposedHeaders="ETag,Content-Length,Content-Range,Accept-Ranges",MaxAgeInSeconds=300 });
            await service.SetPropertiesAsync(properties,cancellationToken:ct);
        }
    }
    public string UploadGrant(string stagingKey, DateTimeOffset expiresAt) => Grant("staging",stagingKey,expiresAt,BlobSasPermissions.Create | BlobSasPermissions.Write);
    public string PlaybackGrant(string canonicalKey, DateTimeOffset expiresAt) => Grant("canonical",canonicalKey,expiresAt,BlobSasPermissions.Read);
    private string Grant(string container, string key, DateTimeOffset expiresAt, BlobSasPermissions permissions)
    {
        var sas = new BlobSasBuilder { BlobContainerName=container,BlobName=key,Resource="b",ExpiresOn=expiresAt };
        sas.SetPermissions(permissions);
        return $"{publicEndpoint}/{container}/{key}?{sas.ToSasQueryParameters(credential)}";
    }
    public async Task<BlobFacts> InspectStaging(string key, CancellationToken ct)
    {
        try { var p = (await service.GetBlobContainerClient("staging").GetBlobClient(key).GetPropertiesAsync(cancellationToken:ct)).Value; return new(p.ContentLength,p.ETag.ToString(),p.ContentType); }
        catch (RequestFailedException e) when (e.Status == 404) { throw new DomainError(422,"upload_missing","Chưa tìm thấy file upload."); }
    }
    public async Task Freeze(string stagingKey, string expectedETag, string canonicalKey, CancellationToken ct)
    {
        var target = service.GetBlobContainerClient("canonical").GetBlobClient(canonicalKey);
        if (await target.ExistsAsync(ct))
        {
            var props = (await target.GetPropertiesAsync(cancellationToken:ct)).Value;
            DomainError.Require(props.Metadata.TryGetValue("sourceetag",out var storedTag) && storedTag == Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(expectedETag)),409,"canonical_mismatch","Nguồn canonical không khớp generation.");
            return;
        }
        // VI: If-Match ngăn xử lý staging đã bị ghi lại sau complete. EN: Pin the source ETag so an old upload grant cannot replace verified input.
        await using var source = (await service.GetBlobContainerClient("staging").GetBlobClient(stagingKey).DownloadStreamingAsync(new BlobDownloadOptions { Conditions=new BlobRequestConditions { IfMatch=new ETag(expectedETag) } },ct)).Value.Content;
        try { await target.UploadAsync(source,new BlobUploadOptions { Conditions=new BlobRequestConditions { IfNoneMatch=ETag.All }, Metadata=new Dictionary<string,string> { ["sourceetag"]=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(expectedETag)) } },ct); }
        catch (RequestFailedException e) when (e.Status is 409 or 412)
        {
            var props=(await target.GetPropertiesAsync(cancellationToken:ct)).Value;
            DomainError.Require(props.Metadata.TryGetValue("sourceetag",out var storedTag) && storedTag==Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(expectedETag)),409,"canonical_mismatch","Canonical source không khớp ETag đã được complete.");
        }
    }
    public async Task Download(string key, string destination, CancellationToken ct) => await service.GetBlobContainerClient("canonical").GetBlobClient(key).DownloadToAsync(destination,ct);
    public async Task DeleteAsset(Guid id,string stagingKey,CancellationToken ct)
    {
        DomainError.Require(id!=Guid.Empty,422,"invalid_asset_id","AssetId không hợp lệ.");
        var container=service.GetBlobContainerClient("canonical");
        await foreach(var blob in container.GetBlobsAsync(BlobTraits.None,BlobStates.None,prefix:$"{id:N}/",cancellationToken:ct))
            await container.DeleteBlobIfExistsAsync(blob.Name,DeleteSnapshotsOption.IncludeSnapshots,cancellationToken:ct);
        await service.GetBlobContainerClient("staging").DeleteBlobIfExistsAsync(stagingKey,DeleteSnapshotsOption.IncludeSnapshots,cancellationToken:ct);
    }
    public async Task Cleanup(Guid id,string stagingKey,string? sourceKey,string? renditionKey,DateTimeOffset olderThan,CancellationToken ct)
    {
        var container=service.GetBlobContainerClient("canonical");
        // VI: Buffer 24 giờ bảo vệ output của claim mới. EN: A 24-hour age buffer protects in-flight output.
        await foreach(var blob in container.GetBlobsAsync(BlobTraits.None,BlobStates.None,prefix:$"{id:N}/",cancellationToken:ct))
            if(blob.Name!=sourceKey && blob.Name!=renditionKey && blob.Properties.CreatedOn<olderThan)
                await container.DeleteBlobIfExistsAsync(blob.Name,DeleteSnapshotsOption.IncludeSnapshots,cancellationToken:ct);
        await service.GetBlobContainerClient("staging").DeleteBlobIfExistsAsync(stagingKey,DeleteSnapshotsOption.IncludeSnapshots,cancellationToken:ct);
    }
    public async Task Upload(string key, string file, CancellationToken ct)
    {
        var target = service.GetBlobContainerClient("canonical").GetBlobClient(key);
        await using var stream = File.OpenRead(file);
        var hash=Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream,ct)); stream.Position=0;
        try { await target.UploadAsync(stream,new BlobUploadOptions { HttpHeaders=new BlobHttpHeaders { ContentType="audio/mpeg" },Conditions=new BlobRequestConditions { IfNoneMatch=ETag.All },Metadata=new Dictionary<string,string> { ["sha256"]=hash } },ct); }
        catch (RequestFailedException e) when (e.Status is 409 or 412)
        {
            var props=(await target.GetPropertiesAsync(cancellationToken:ct)).Value;
            DomainError.Require(props.ContentLength==stream.Length && props.Metadata.TryGetValue("sha256",out var storedHash) && storedHash==hash,409,"rendition_conflict","Rendition hiện có khác nội dung đã xác minh.");
        }
    }
}
