using System.Text.Json;

namespace Tracebag.Api.Security;

public sealed class ErrorHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (TracebagException ex)
        {
            context.Response.StatusCode = ex.StatusCode;
            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(context.Response.Body, new
            {
                error = ex.Code,
                message = ex.Message
            }, JsonOptions, context.RequestAborted);
        }
        catch (BadHttpRequestException ex)
        {
            context.Response.StatusCode = ex.StatusCode;
            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(context.Response.Body, new
            {
                error = ex.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "request_too_large"
                    : "bad_request",
                message = ex.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "The request body is too large."
                    : "The request could not be read."
            }, JsonOptions, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled Tracebag error.");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(context.Response.Body, new
            {
                error = "internal_error",
                message = "The Tracebag backend failed while processing the request."
            }, JsonOptions, context.RequestAborted);
        }
    }
}
