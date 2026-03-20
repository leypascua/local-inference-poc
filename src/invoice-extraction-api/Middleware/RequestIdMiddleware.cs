namespace invoice_extraction_api.Middleware;

public sealed class RequestIdMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context)
    {
        var now = DateTime.UtcNow;
        var stamp = now.ToString("yyyyMMdd'T'HHmmss");
        var requestId = $"{stamp}_{Guid.CreateVersion7()}";

        context.Items[HttpContextExtensions.RequestIdKey] = requestId;
        context.Response.Headers["x-request-id"] = requestId;

        await next(context);
    }
}
