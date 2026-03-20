using invoice_extraction_api.Contracts;
using invoice_extraction_api.Models;

namespace invoice_extraction_api.Middleware;

public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task Invoke(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException ex)
        {
            await WriteErrorAsync(context, ex.StatusCode, ex.Code, ex.Message, ex.Details);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception while processing request");
            await WriteErrorAsync(context, 500, "internal_error", "Unexpected server error.");
        }
    }

    private static Task WriteErrorAsync(HttpContext context, int statusCode, string code, string message, object? details = null)
    {
        context.Response.StatusCode = statusCode;
        var envelope = new ErrorEnvelope(
            context.GetRequestId(),
            new ErrorBody(code, message, details)
        );
        return context.Response.WriteAsJsonAsync(envelope);
    }
}
