using System.Net;
using FluentAssertions;

namespace BusinessManagement.Tests.Api;

[Collection("Api")]
public sealed class ApiSmokeTests(ApiFixture fx)
{
    [Fact]
    public async Task Health_endpoint_is_reachable_anonymously()
    {
        var res = await fx.CreateClient().GetAsync("/health");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Swagger_document_is_generated_in_development()
    {
        var res = await fx.CreateClient().GetAsync("/swagger/v1/swagger.json");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await res.Content.ReadAsStringAsync();
        json.Should().Contain("Trvyam Management API").And.Contain("/api/v1/auth/login");
    }

    [Theory]
    [InlineData("/api/v1/me")]
    [InlineData("/api/v1/businesses/1/my-permissions")]
    public async Task Protected_endpoints_reject_anonymous_callers(string url)
    {
        var res = await fx.CreateClient().GetAsync(url);
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
