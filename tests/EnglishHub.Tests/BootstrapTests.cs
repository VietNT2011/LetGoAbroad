using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

public sealed class BootstrapTests
{
    // VI: Kiểm tra host thật và OpenAPI. EN: Boot the actual host and verify its public contract.
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/openapi/v1.json")]
    public async Task Host_exposes_health_and_contract(string route)
    {
        var app = new TestApp();
        await using (app)
        {
            using var response = await app.CreateClient().GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        // VI: await using cũng phải dọn DB, không chỉ xUnit fixture teardown. EN: Directly-created hosts must clean their database as well as xUnit fixtures.
        if (app.SqlDatabaseName is { } database)
        {
            var sql = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("EH_TEST_SQL")) { InitialCatalog = "master" };
            await using var master = new Microsoft.Data.SqlClient.SqlConnection(sql.ConnectionString); await master.OpenAsync();
            await using var command = new Microsoft.Data.SqlClient.SqlCommand("SELECT DB_ID(@name)", master); command.Parameters.AddWithValue("@name", database);
            Assert.Equal(DBNull.Value, await command.ExecuteScalarAsync());
        }
        else Assert.False(File.Exists(app.DatabasePath));
    }
}
