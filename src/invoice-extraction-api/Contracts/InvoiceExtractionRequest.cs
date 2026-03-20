using System.Text.Json.Serialization;

namespace invoice_extraction_api.Contracts;

public sealed class InvoiceExtractionRequest
{
    [JsonPropertyName("content")]
    public List<InvoiceContentItem>? Content { get; init; }
}

public sealed class InvoiceContentItem
{
    [JsonPropertyName("file_url")]
    public string? FileUrl { get; init; }
    [JsonPropertyName("name")]
    public string? Name { get; init; }
    [JsonPropertyName("content_type")]
    public string? ContentType { get; init; }
}

public sealed record ValidationError(string Path, string Message);

public static class InvoiceExtractionRequestValidator
{
    public static readonly HashSet<string> SupportedContentTypes =
    [
        "image/jpeg",
        "image/png",
        "image/gif",
        "application/pdf"
    ];

    public static bool IsSupportedImageType(string value)
    {
        return value is "image/jpeg" or "image/png" or "image/gif";
    }

    public static List<ValidationError> Validate(InvoiceExtractionRequest? request)
    {
        var errors = new List<ValidationError>();

        if (request?.Content is null)
        {
            errors.Add(new ValidationError("content", "Required"));
            return errors;
        }

        if (request.Content.Count == 0)
        {
            errors.Add(new ValidationError("content", "content must include at least one item."));
            return errors;
        }

        for (var i = 0; i < request.Content.Count; i++)
        {
            var item = request.Content[i];
            var prefix = $"content.{i}";

            if (string.IsNullOrWhiteSpace(item.FileUrl))
            {
                errors.Add(new ValidationError($"{prefix}.file_url", "file_url is required."));
            }

            if (string.IsNullOrWhiteSpace(item.Name))
            {
                errors.Add(new ValidationError($"{prefix}.name", "name is required."));
            }
            else if (item.Name.Length > 255)
            {
                errors.Add(new ValidationError($"{prefix}.name", "name must be 255 characters or fewer."));
            }

            if (string.IsNullOrWhiteSpace(item.ContentType) || !SupportedContentTypes.Contains(item.ContentType))
            {
                errors.Add(new ValidationError($"{prefix}.content_type", "Invalid option"));
            }
        }

        return errors;
    }
}
