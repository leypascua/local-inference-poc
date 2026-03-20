namespace invoice_extraction_api.Middleware;

public static class HttpContextExtensions
{
    public const string RequestIdKey = "request_id";

    public static string GetRequestId(this HttpContext context)
    {
        if (context.Items.TryGetValue(RequestIdKey, out var value) && value is string requestId)
        {
            return requestId;
        }

        return "unknown";
    }
}
