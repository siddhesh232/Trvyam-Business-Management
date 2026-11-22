using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BusinessManagement.Tests.Api;

public sealed class TestApiFactory : WebApplicationFactory<Program>
{
    public TestApiFactory()
    {
        // Program.cs reads Jwt settings eagerly while building the host, before WebApplicationFactory's
        // ConfigureAppConfiguration callbacks run, so test secrets must come from environment variables.
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "test-only-signing-key-0123456789-abcdef-0123456789");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
}

public sealed class ApiSmokeTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    [Fact]
    public async Task Health_endpoint_is_reachable_anonymously()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var res = await client.GetAsync("/health");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Swagger_document_is_generated_in_development()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/swagger/v1/swagger.json");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain("Trvyam Management API");
    }
}
