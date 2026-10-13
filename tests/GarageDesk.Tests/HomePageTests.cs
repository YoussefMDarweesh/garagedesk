using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GarageDesk.Tests;

public class HomePageTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    [Fact]
    public async Task Home_page_returns_200()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
