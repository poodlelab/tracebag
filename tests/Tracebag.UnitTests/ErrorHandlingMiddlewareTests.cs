using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Tracebag.Api.Security;

namespace Tracebag.UnitTests;

public sealed class ErrorHandlingMiddlewareTests
{
    [Fact]
    public async Task PreservesPayloadTooLargeAsMachineReadableClientError()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new BadHttpRequestException("internal server detail", StatusCodes.Status413PayloadTooLarge),
            NullLogger<ErrorHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
        Assert.Equal("request_too_large", response.GetProperty("error").GetString());
        Assert.Equal("The request body is too large.", response.GetProperty("message").GetString());
    }
}
