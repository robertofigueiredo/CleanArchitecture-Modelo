using System.Net;
using System.Net.Http.Json;

namespace IntegrationTests.Users;

public sealed class LoginValidationTests(IntegrationTestWebAppFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task Login_Should_ReturnBadRequest_WhenEmailIsInvalid()
    {
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "users/login",
            new { email = "not-an-email", password = "Password123" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_Should_ReturnBadRequest_WhenPasswordIsEmpty()
    {
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "users/login",
            new { email = "test@example.com", password = string.Empty });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
