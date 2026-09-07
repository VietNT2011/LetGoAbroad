using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using System.Text.Json;

/// <summary>VI: Test dùng DB quan hệ riêng, không EF InMemory. EN: Each fixture has an isolated relational database, never EF InMemory.</summary>
public sealed class TestApp : WebApplicationFactory<Program>
{
    public string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), $"englishhub-test-{Guid.NewGuid():N}.db");
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        var sql = Environment.GetEnvironmentVariable("EH_TEST_SQL");
        if (sql is not null) { var cs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(sql) { InitialCatalog = $"EnglishHubTest_{Guid.NewGuid():N}" }; sql = cs.ConnectionString; }
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
            ["Database:Provider"] = Environment.GetEnvironmentVariable("EH_TEST_SQL") is null ? "Sqlite" : "SqlServer",
            ["ConnectionStrings:Database"] = sql ?? $"Data Source={DatabasePath}",
            ["Database:Initialize"] = "true", ["Seed:Demo"] = "true", ["FeatureFlags:EnableCommunity"] = "true"
        }));
    }

    public async Task<HttpClient> Login(string email = "learner@englishhub.local")
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        await Csrf(client);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "EnglishHub@2026" });
        login.EnsureSuccessStatusCode();
        // VI: Token CSRF phải lấy lại sau đổi danh tính. EN: Refresh the identity-bound token after signing in.
        await Csrf(client);
        return client;
    }

    public static async Task Csrf(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("requestToken").GetString());
    }
}
