using System.Text.Json;
using invoice_extraction_api.Contracts;
using invoice_extraction_api.Middleware;
using invoice_extraction_api.Models;
using invoice_extraction_api.Services;

namespace invoice_extraction_api.Endpoints;

public static class InvoiceEndpoints
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<IResult> ExtractAsync(HttpContext context, ExtractionPipelineService pipeline)
    {
        var contentType = context.Request.ContentType ?? string.Empty;
        if (!contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            throw new AppException(415, "unsupported_media_type", "Content-Type must be application/json.");
        }

        InvoiceExtractionRequest? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<InvoiceExtractionRequest>(context.Request.Body, SerializerOptions, context.RequestAborted);
        }
        catch (JsonException)
        {
            throw new AppException(400, "invalid_json", "Request body must be valid JSON.");
        }

        var validationErrors = InvoiceExtractionRequestValidator.Validate(payload);
        if (validationErrors.Count > 0)
        {
            throw new AppException(400, "invalid_request_body", "Request body did not match the expected schema.", validationErrors);
        }

        var extraction = await pipeline.RunAsync(context.GetRequestId(), payload!, context.RequestAborted);
        return Results.Json(new SuccessEnvelope(context.GetRequestId(), extraction));
    }
}
