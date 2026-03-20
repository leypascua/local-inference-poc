using System.Text.Json.Serialization;

namespace invoice_extraction_api.Contracts;

public sealed record ErrorBody(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("details")] object? Details = null);

public sealed record ErrorEnvelope(
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("error")] ErrorBody Error);

public sealed record SuccessEnvelope(
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("response")] ExtractionResponse Response);

public sealed class ExtractionResponse
{
    [JsonPropertyName("results")]
    public required List<ExtractionDocument> Results { get; init; }
}

public sealed class ExtractionDocument
{
    [JsonPropertyName("document_title")]
    public string? DocumentTitle { get; init; }
    [JsonPropertyName("invoice_date")]
    public string? InvoiceDate { get; init; }
    [JsonPropertyName("invoice_number")]
    public string? InvoiceNumber { get; init; }
    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; init; }
    [JsonPropertyName("gross_amount")]
    public decimal? GrossAmount { get; init; }
    [JsonPropertyName("seller")]
    public required Party Seller { get; init; }
    [JsonPropertyName("end_customer")]
    public required EndCustomer EndCustomer { get; init; }
    [JsonPropertyName("purchases")]
    public required List<Purchase> Purchases { get; init; }
}

public class Party
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
    [JsonPropertyName("city")]
    public string? City { get; init; }
    [JsonPropertyName("state")]
    public string? State { get; init; }
    [JsonPropertyName("country")]
    public string? Country { get; init; }
}

public sealed class EndCustomer : Party
{
    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

public sealed class Purchase
{
    [JsonPropertyName("quantity")]
    public decimal? Quantity { get; init; }
    [JsonPropertyName("part_number")]
    public string? PartNumber { get; init; }
    [JsonPropertyName("description")]
    public string? Description { get; init; }
    [JsonPropertyName("serial_numbers")]
    public required List<string> SerialNumbers { get; init; }
    [JsonPropertyName("unit_price")]
    public decimal? UnitPrice { get; init; }
}
