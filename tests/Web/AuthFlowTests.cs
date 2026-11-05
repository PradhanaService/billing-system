using System.Net;

namespace WaterBilling.Tests.Web;

// End-to-end login rules, through real HTTP requests.
public class AuthFlowTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public AuthFlowTests(TestAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/")]
    [InlineData("/admin/users")]
    public async Task NFR_SEC_05_Pages_Need_Login(string url)
    {
        var response = await _factory.NewClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account/login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Wrong_Password_Shows_Generic_Error_And_No_Cookie()
    {
        await _factory.AdminClientAsync();   // make sure the seed password was already changed
        var client = _factory.NewClient();

        var response = await TestAppFactory.PostFormAsync(client, "/account/login", "login",
            ("Input.UserName", "admin"), ("Input.Password", "wrong-password1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Wrong user name or password", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Logged_In_Admin_Sees_Dashboard()
    {
        var client = await _factory.AdminClientAsync();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Dashboard", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_Ignores_ReturnUrl_Pointing_To_Another_Site()
    {
        await _factory.AdminClientAsync();
        var client = _factory.NewClient();

        var response = await TestAppFactory.PostFormAsync(client, "/account/login?returnUrl=%2F%2Fevil.example.com", "login",
            ("Input.UserName", "admin"), ("Input.Password", TestAppFactory.AdminPassword));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var target = new Uri(new Uri("http://localhost/"), response.Headers.Location!);
        Assert.Equal("localhost", target.Host);   // stayed on this app
        Assert.Equal("/", target.AbsolutePath);
    }

    [Fact]
    public async Task Logout_Without_Antiforgery_Token_Is_Refused()
    {
        var client = await _factory.AdminClientAsync();

        var response = await client.PostAsync("/account/logout",
            new FormUrlEncodedContent([new("returnUrl", "/")]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);   // still logged in
    }
}

// A fresh app, so the seed admin still has the temporary password.
public class FirstLoginTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public FirstLoginTests(TestAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FR_ADM_02_Temporary_Password_Forces_Change_Before_Anything_Else()
    {
        var client = _factory.NewClient();
        var login = await TestAppFactory.PostFormAsync(client, "/account/login", "login",
            ("Input.UserName", "admin"), ("Input.Password", TestAppFactory.SeedPassword));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        var dashboard = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, dashboard.StatusCode);
        Assert.Equal("/account/change-password", dashboard.Headers.Location!.OriginalString);
    }
}
