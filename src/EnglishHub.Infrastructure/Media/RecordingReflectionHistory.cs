using System.Text.Json;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Media;

public sealed record RecordingSourceView(Guid VersionId, string Title, int Version, Guid? MediaAssetId, bool AudioAvailable, bool Archived, Guid? SegmentId, int? SegmentOrder, long? StartMs, long? EndMs);
public sealed record SetRecordingSource(Guid ClientRequestId, string? ExpectedEtag, Guid? SourceContentVersionId, Guid? SourceSegmentId = null);
public sealed record RecordingRevisionView(int Number, string Kind, string Notes, int? Clarity, int? Rhythm, DateTimeOffset UpdatedAt, DateTimeOffset CapturedAt, RecordingSourceView? Source);

public sealed partial class RecordingReflectionService
{
    /// <summary>VI: Ghim/đổi nguồn là lệnh riêng, không tự lưu nháp nhận xét. EN: Binding a sample is a separate command and never implicitly saves the client's note draft.</summary>
    public Task<RecordingReflectionView> SetSource(Guid user, Guid assetId, SetRecordingSource input) => tx.Run($"media-quota:{user}", async () =>
    {
        var asset = await Owned(user, assetId); var hash = Receipts.Hash(new { assetId, input });
        var duplicate = await receipts.Find(user, "recording-source", input.ClientRequestId, hash);
        if (duplicate is not null) return JsonSerializer.Deserialize<RecordingReflectionView>(duplicate.ResponseJson)!;
        DomainError.Require(input.SourceSegmentId is null || input.SourceContentVersionId is not null, 422, "invalid_recording_source", "Đoạn nghe cần đúng phiên bản bài mẫu.");
        var row = await db.RecordingReflections.SingleOrDefaultAsync(x => x.MediaAssetId == assetId);
        if (row is null)
        {
            DomainError.Require(input.ExpectedEtag is null, 412, "stale_version", "Tải lại metadata trước khi gắn mẫu.");
            DomainError.Require(asset.Status == MediaStatus.Ready, 409, "recording_not_ready", "Cần bản thu Ready để tạo ngữ cảnh mới.");
            DomainError.Require(input.SourceContentVersionId is not null, 422, "empty_recording_source", "Chọn bài mẫu để bắt đầu.");
            row = new RecordingReflection { MediaAssetId = assetId };
        }
        else Etags.Check(row, input.ExpectedEtag);
        if (row.SourceContentVersionId != input.SourceContentVersionId || row.SourceSegmentId != input.SourceSegmentId)
        {
            // VI: Cùng thứ tự quota → catalog; không giữ blob I/O trong transaction.
            // EN: Follow quota → catalog lock order without performing blob I/O in the transaction.
            await tx.Lock("catalog");
            if (input.SourceContentVersionId is { } versionId)
            {
                var allowed = await db.ContentVersions.AnyAsync(v => v.Id == versionId && v.Root.Kind == ContentKind.Lesson && !v.IsFixture
                    && (v.Status == PublicationStatus.Published || v.Status == PublicationStatus.Archived && (row.SourceContentVersionId == versionId || db.RecordingReflectionRevisions.Any(h => h.ReflectionId == row.Id && h.SourceContentVersionId == versionId) || db.ListeningSessions.Any(s => s.UserId == user && s.ContentVersionId == versionId)))
                    && db.MediaAssets.Any(a => a.Id == v.MediaAssetId && a.Purpose == "lesson_audio" && a.Status == MediaStatus.Ready && a.DurationMs > 0));
                DomainError.Require(allowed, 422, "invalid_recording_source", "Chọn bài học có audio Ready và quyền xem mẫu phù hợp.");
                DomainError.Require(input.SourceSegmentId is null || await db.TranscriptSegments.AnyAsync(s => s.Id == input.SourceSegmentId && s.ContentVersionId == versionId), 422, "invalid_recording_segment", "Đoạn nghe không thuộc phiên bản bài mẫu.");
            }
            var added = db.Entry(row).State == EntityState.Detached;
            if (added) db.RecordingReflections.Add(row);
            BeforeChange(row); row.SourceContentVersionId = input.SourceContentVersionId; row.SourceSegmentId = input.SourceSegmentId; row.UpdatedAt = clock.UtcNow;
            Append(row, "source"); db.AuditEntries.Add(new AuditEntry { ActorId = user, Action = "recording.source_changed", ResourceId = row.Id, OccurredAt = clock.UtcNow });
            await db.SaveChangesAsync();
        }
        var result = await View(asset, row); receipts.Save(user, "recording-source", input.ClientRequestId, hash, row.Id, result); return result;
    });

    public async Task<object> History(Guid user, Guid assetId, int? before)
    {
        await Owned(user, assetId);
        DomainError.Require(before is null or >= 0, 422, "invalid_history_cursor", "Mốc lịch sử không hợp lệ.");
        var rows = await db.RecordingReflectionRevisions.AsNoTracking().Where(h => h.Reflection.MediaAssetId == assetId && (before == null || h.Number < before))
            .OrderByDescending(h => h.Number).Take(31).ToArrayAsync();
        var sources = await Sources(rows.Select(x => (x.SourceContentVersionId, x.SourceSegmentId)));
        return new { items = rows.Take(30).Select(h => new RecordingRevisionView(h.Number, h.Kind, h.Notes, h.Clarity, h.Rhythm, h.UpdatedAt, h.CapturedAt, Source(sources, h.SourceContentVersionId, h.SourceSegmentId))), nextBefore = rows.Length > 30 ? rows[29].Number : (int?)null };
    }

    private void BeforeChange(RecordingReflection row)
    {
        DomainError.Require(row.Revision < int.MaxValue, 409, "reflection_revision_limit", "Nhận xét đã đạt giới hạn phiên bản.");
        // VI: Không backfill lịch sử giả. Chỉ chụp giá trị cũ chưa có version tại lần sửa thật đầu tiên.
        // EN: Do not invent an edit history; capture an unversioned old value only when its first real change occurs.
        if (row.Revision == 0 && db.Entry(row).State != EntityState.Added)
            db.RecordingReflectionRevisions.Add(Capture(row, "legacy_snapshot"));
    }
    private void Append(RecordingReflection row, string kind) { row.Revision++; db.RecordingReflectionRevisions.Add(Capture(row, kind)); }
    private RecordingReflectionRevision Capture(RecordingReflection row, string kind) => new()
    {
        ReflectionId = row.Id, Number = row.Revision, Kind = kind, Notes = row.Notes, Clarity = row.Clarity, Rhythm = row.Rhythm,
        UpdatedAt = row.UpdatedAt, CapturedAt = clock.UtcNow, SourceContentVersionId = row.SourceContentVersionId, SourceSegmentId = row.SourceSegmentId
    };

    private async Task<Dictionary<(Guid, Guid?), RecordingSourceView>> Sources(IEnumerable<(Guid? Version, Guid? Segment)> requested)
    {
        var pairs = requested.Where(x => x.Version.HasValue).Select(x => (Version: x.Version!.Value, x.Segment)).Distinct().ToArray();
        var result = new Dictionary<(Guid, Guid?), RecordingSourceView>(); if (pairs.Length == 0) return result;
        var ids = pairs.Select(x => x.Version).Distinct().ToArray();
        var versions = await db.ContentVersions.AsNoTracking().Where(v => ids.Contains(v.Id)).Select(v => new { v.Id, v.Title, v.Version, v.MediaAssetId, v.Status }).ToDictionaryAsync(v => v.Id);
        var mediaIds = versions.Values.Where(v => v.MediaAssetId != null).Select(v => v.MediaAssetId!.Value).ToArray();
        var ready = await db.MediaAssets.AsNoTracking().Where(a => mediaIds.Contains(a.Id) && a.Purpose == "lesson_audio" && a.Status == MediaStatus.Ready).Select(a => a.Id).ToArrayAsync();
        var segmentIds = pairs.Where(x => x.Segment != null).Select(x => x.Segment!.Value).ToArray();
        var segments = await db.TranscriptSegments.AsNoTracking().Where(s => segmentIds.Contains(s.Id)).Select(s => new { s.Id, s.Order, s.StartMs, s.EndMs }).ToDictionaryAsync(s => s.Id);
        foreach (var (versionId, segmentId) in pairs)
        {
            if (!versions.TryGetValue(versionId, out var version)) continue;
            var segment = segmentId is { } sid ? segments.GetValueOrDefault(sid) : null;
            result[(versionId, segmentId)] = new(versionId, version.Title, version.Version, version.MediaAssetId, version.MediaAssetId is { } mid && ready.Contains(mid), version.Status == PublicationStatus.Archived, segmentId, segment?.Order, segment?.StartMs, segment?.EndMs);
        }
        return result;
    }
    private static RecordingSourceView? Source(Dictionary<(Guid, Guid?), RecordingSourceView> values, Guid? version, Guid? segment) => version is { } id ? values.GetValueOrDefault((id, segment)) : null;
}
