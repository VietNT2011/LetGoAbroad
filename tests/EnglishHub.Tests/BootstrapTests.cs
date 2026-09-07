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
        await using var app = new TestApp();
        using var response = await app.CreateClient().GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
