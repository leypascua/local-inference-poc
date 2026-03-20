using System.Text.Json.Nodes;
using invoice_extraction_api.Contracts;
using invoice_extraction_api.Models;

namespace invoice_extraction_api.Services;

public sealed class ExtractionNormalizer
{
    public ExtractionResponse NormalizeAndValidate(JsonNode modelOutput)
    {
        var normalizedCandidate = NormalizeExtractionCandidate(modelOutput);
        if (normalizedCandidate is not JsonObject root || root.Count != 1 || root["results"] is not JsonArray resultsNode)
        {
            throw new AppException(502, "invalid_model_output", "Model output did not match the extraction schema.");
        }

        var results = new List<ExtractionDocument>();
        foreach (var resultNode in resultsNode)
        {
            results.Add(ParseDocument(resultNode));
        }

        return new ExtractionResponse { Results = results };
    }

    private static ExtractionDocument ParseDocument(JsonNode? node)
    {
        var record = ExpectObject(node, "results[]");
        EnsureExactKeys(record, "results[]",
        [
            "document_title", "invoice_date", "invoice_number", "currency_code", "gross_amount", "seller", "end_customer", "purchases"
        ]);

        var purchasesNode = record["purchases"] as JsonArray ?? throw InvalidSchema("results[].purchases");
        var purchases = purchasesNode.Select(ParsePurchase).ToList();

        return new ExtractionDocument
        {
            DocumentTitle = AsNullableString(record["document_title"]),
            InvoiceDate = AsNullableString(record["invoice_date"]),
            InvoiceNumber = AsNullableString(record["invoice_number"]),
            CurrencyCode = AsNullableString(record["currency_code"]),
            GrossAmount = AsNullableDecimal(record["gross_amount"]),
            Seller = ParseParty(record["seller"], includeEmail: false),
            EndCustomer = ParseEndCustomer(record["end_customer"]),
            Purchases = purchases
        };
    }

    private static Party ParseParty(JsonNode? node, bool includeEmail)
    {
        var record = ExpectObject(node, "party");
        var expected = includeEmail
            ? new[] { "name", "city", "state", "country", "email" }
            : new[] { "name", "city", "state", "country" };
        EnsureExactKeys(record, "party", expected);

        return new Party
        {
            Name = AsNullableString(record["name"]),
            City = AsNullableString(record["city"]),
            State = AsNullableString(record["state"]),
            Country = AsNullableString(record["country"]),
        };
    }

    private static EndCustomer ParseEndCustomer(JsonNode? node)
    {
        var record = ExpectObject(node, "end_customer");
        EnsureExactKeys(record, "end_customer", ["name", "city", "state", "country", "email"]);
        return new EndCustomer
        {
            Name = AsNullableString(record["name"]),
            City = AsNullableString(record["city"]),
            State = AsNullableString(record["state"]),
            Country = AsNullableString(record["country"]),
            Email = AsNullableString(record["email"])
        };
    }

    private static Purchase ParsePurchase(JsonNode? node)
    {
        var record = ExpectObject(node, "purchase");
        EnsureExactKeys(record, "purchase", ["quantity", "part_number", "description", "serial_numbers", "unit_price"]);

        List<string> serialNumbers;
        if (record["serial_numbers"] is JsonArray serialNode)
        {
            var allStrings = serialNode.All(item => item is JsonValue val && val.TryGetValue<string>(out _));
            serialNumbers = allStrings
                ? serialNode.Select(item => item!.GetValue<string>().Trim()).ToList()
                : [];
        }
        else
        {
            serialNumbers = [];
        }

        return new Purchase
        {
            Quantity = AsNullableDecimal(record["quantity"]),
            PartNumber = AsNullableString(record["part_number"]),
            Description = AsNullableString(record["description"]),
            SerialNumbers = serialNumbers,
            UnitPrice = AsNullableDecimal(record["unit_price"])
        };
    }

    private static JsonNode NormalizeExtractionCandidate(JsonNode value)
    {
        if (value is not JsonObject record || record["results"] is not JsonArray results)
        {
            return value;
        }

        if (results.Count == 0 || !results.All(LooksLikePurchaseRow))
        {
            return value;
        }

        return new JsonObject
        {
            ["results"] = new JsonArray
            {
                new JsonObject
                {
                    ["document_title"] = AsJsonValue(AsNullableString(record["document_title"])),
                    ["invoice_date"] = AsJsonValue(AsNullableString(record["invoice_date"])),
                    ["invoice_number"] = AsJsonValue(AsNullableString(record["invoice_number"])),
                    ["currency_code"] = AsJsonValue(AsNullableString(record["currency_code"])),
                    ["gross_amount"] = AsJsonValue(AsNullableDecimal(record["gross_amount"] ?? record["total_amount"])),
                    ["seller"] = NormalizeParty(record["seller"], includeEmail: false),
                    ["end_customer"] = NormalizeParty(record["end_customer"], includeEmail: true),
                    ["purchases"] = new JsonArray(results.Select(item => NormalizePurchase(item)).ToArray())
                }
            }
        };
    }

    private static JsonObject NormalizeParty(JsonNode? node, bool includeEmail)
    {
        var record = node as JsonObject;
        var normalized = new JsonObject
        {
            ["name"] = AsJsonValue(AsNullableString(record?["name"])),
            ["city"] = AsJsonValue(AsNullableString(record?["city"])),
            ["state"] = AsJsonValue(AsNullableString(record?["state"])),
            ["country"] = AsJsonValue(AsNullableString(record?["country"]))
        };

        if (includeEmail)
        {
            normalized["email"] = AsJsonValue(AsNullableString(record?["email"]));
        }

        return normalized;
    }

    private static JsonObject NormalizePurchase(JsonNode? node)
    {
        var record = node as JsonObject;
        var serialNumbers = record?["serial_numbers"] as JsonArray;
        var normalizedSerialNumbers = new JsonArray();
        if (serialNumbers is not null)
        {
            foreach (var item in serialNumbers)
            {
                var normalized = AsNullableString(item);
                if (normalized is not null)
                {
                    normalizedSerialNumbers.Add(normalized);
                }
            }
        }

        return new JsonObject
        {
            ["quantity"] = AsJsonValue(AsNullableDecimal(record?["quantity"])),
            ["part_number"] = AsJsonValue(AsNullableString(record?["part_number"])),
            ["description"] = AsJsonValue(AsNullableString(record?["description"] ?? record?["product_name"])),
            ["serial_numbers"] = normalizedSerialNumbers,
            ["unit_price"] = AsJsonValue(AsNullableDecimal(record?["unit_price"] ?? record?["amount"]))
        };
    }

    private static bool LooksLikePurchaseRow(JsonNode? value)
    {
        if (value is not JsonObject obj)
        {
            return false;
        }

        return obj.ContainsKey("product_name")
            || obj.ContainsKey("amount")
            || (obj.ContainsKey("quantity") && (obj.ContainsKey("unit_price") || obj.ContainsKey("part_number")));
    }

    private static string? AsNullableString(JsonNode? value)
    {
        if (value is not JsonValue val || !val.TryGetValue<string>(out var text))
        {
            return null;
        }

        var trimmed = text.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static decimal? AsNullableDecimal(JsonNode? value)
    {
        if (value is JsonValue val)
        {
            if (val.TryGetValue<decimal>(out var direct))
            {
                return direct;
            }

            if (val.TryGetValue<double>(out var dbl) && double.IsFinite(dbl))
            {
                return (decimal)dbl;
            }

            if (val.TryGetValue<string>(out var str))
            {
                var normalized = str.Replace(",", string.Empty).Trim();
                if (normalized.Length == 0)
                {
                    return null;
                }

                if (decimal.TryParse(normalized, out var parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    private static JsonObject ExpectObject(JsonNode? node, string path)
    {
        return node as JsonObject ?? throw InvalidSchema(path);
    }

    private static void EnsureExactKeys(JsonObject obj, string path, IEnumerable<string> required)
    {
        var expected = required.ToHashSet(StringComparer.Ordinal);
        if (obj.Count != expected.Count)
        {
            throw InvalidSchema(path);
        }

        foreach (var key in expected)
        {
            if (!obj.ContainsKey(key))
            {
                throw InvalidSchema($"{path}.{key}");
            }
        }
    }

    private static JsonNode? AsJsonValue(string? value)
    {
        return JsonValue.Create(value);
    }

    private static JsonNode? AsJsonValue(decimal? value)
    {
        return JsonValue.Create(value);
    }

    private static AppException InvalidSchema(string path)
    {
        return new AppException(502, "invalid_model_output", "Model output did not match the extraction schema.", new { path });
    }
}
