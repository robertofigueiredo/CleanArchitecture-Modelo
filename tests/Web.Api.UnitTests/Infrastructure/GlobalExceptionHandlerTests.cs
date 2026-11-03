using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Web.Api.Contracts;
using Web.Api.Infrastructure;

namespace Web.Api.UnitTests.Infrastructure;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_UnexpectedException_Writes500WithGenericErrorEnvelope()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var httpContext = new DefaultHttpContext();
        using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        bool handled = await handler.TryHandleAsync(httpContext, new InvalidOperationException("boom"), CancellationToken.None);

        handled.ShouldBeTrue();
        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status500InternalServerError);

        responseBody.Seek(0, SeekOrigin.Begin);
        var body = await JsonSerializer.DeserializeAsync<ErrorResponse>(
            responseBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        body!.Errors.Count.ShouldBe(1);
        body.Errors.Single().Detail.ShouldNotContain("boom"); // never leak exception details to the client
    }
}
