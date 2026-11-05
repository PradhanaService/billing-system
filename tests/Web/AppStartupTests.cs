using System.Net;

namespace WaterBilling.Tests.Web;

// The app starts and answers safely.
public class AppStartupTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public AppStartupTests(TestAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_Page_Loads()
    {
        var response = await _factory.NewClient().GetAsync("/account/login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Shree Karunambika Traders", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Responses_Carry_SecurityHeaders()
    {
        var response = await _factory.NewClient().GetAsync("/account/login");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
    }

    [Fact]
    public async Task Request_From_Unknown_Host_Is_Rejected()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/account/login");
        request.Headers.Host = "evil.example.com";

        var response = await _factory.NewClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_Page_Returns_404_With_Friendly_Message()
    {
        var client = await _factory.AdminClientAsync();

        var response = await client.GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found", await response.Content.ReadAsStringAsync());
    }
}
