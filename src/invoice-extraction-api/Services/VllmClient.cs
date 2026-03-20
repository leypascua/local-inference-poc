using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using invoice_extraction_api.Configuration;
using invoice_extraction_api.Models;

namespace invoice_extraction_api.Services;

public sealed partial class VllmClient(IHttpClientFactory httpClientFactory)
{
    private const string UserInstruction = "Extract the uploaded documents into one JSON object that matches the schema exactly.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _schemaAsset = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "invoice-extraction-output-schema.json"));
    private readonly string _promptAsset = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "invoice-extraction-prompt.md")).Trim();

    public async Task<JsonNode> ExtractAsync(InvoiceExtractionOptions options, List<PreparedImageFile> images, CancellationToken cancellationToken)
    {
        var imageParts = images.Select(image =>
            new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject { ["url"] = new Uri(image.FilePath).AbsoluteUri }
            }).ToList();

        var modes = new[] { "json_schema", "response_format", "prompt_only" };
        AppException? lastError = null;

        foreach (var mode in modes)
        {
            var body = BuildRequestBody(options, imageParts, mode);
            using var response = await CreateChatCompletionAsync(options, body, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if ((int)response.StatusCode == 400 && StructuredOutputError().IsMatch(errorBody))
                {
                    lastError = new AppException(502, "llama_upstream_error", $"vLLM rejected {mode} structured output mode.", new { body = errorBody, mode });
                    continue;
                }

                throw new AppException(502, "llama_upstream_error", $"vLLM returned HTTP {(int)response.StatusCode}.", string.IsNullOrWhiteSpace(errorBody) ? new { mode } : new { body = errorBody, mode });
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonNode>(JsonOptions, cancellationToken);
            var content = StripJsonCodeFence(ExtractMessageContent(payload));
            if (string.IsNullOrWhiteSpace(content))
            {
                lastError = new AppException(502, "invalid_model_output", "vLLM returned empty completion content.", new { mode });
                continue;
            }

            try
            {
                return ParseStructuredContent(content);
            }
            catch (AppException ex)
            {
                lastError = new AppException(ex.StatusCode, ex.Code, ex.Message, new { mode, details = ex.Details });
            }
        }

        throw lastError ?? new AppException(502, "invalid_model_output", "vLLM did not return valid structured JSON.");
    }

    public async Task<bool> CheckReadinessAsync(InvoiceExtractionOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            using var timeout = new CancellationTokenSource(Math.Min(options.RemoteFetchTimeoutMs, 5000));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var response = await client.GetAsync($"{options.VllmBaseUrl}/v1/models", linked.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<HttpResponseMessage> CreateChatCompletionAsync(InvoiceExtractionOptions options, JsonObject body, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        using var timeout = new CancellationTokenSource(options.RequestTimeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{options.VllmBaseUrl}/v1/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        try
        {
            return await client.SendAsync(request, linked.Token);
        }
        catch (Exception ex)
        {
            throw new AppException(502, "llama_upstream_error", "Unable to reach vLLM.", new { cause = ex.Message });
        }
    }

    private JsonObject BuildRequestBody(InvoiceExtractionOptions options, List<JsonObject> imageParts, string mode)
    {
        var body = new JsonObject
        {
            ["model"] = options.VllmModel,
            ["temperature"] = 0,
            ["max_tokens"] = 1024,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = BuildPrompt(mode)
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = BuildUserContent(imageParts)
                }
            }
        };

        if (mode == "json_schema")
        {
            body["json_schema"] = JsonNode.Parse(_schemaAsset);
        }

        if (mode == "response_format")
        {
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = "invoice_extraction_response",
                    ["strict"] = true,
                    ["schema"] = JsonNode.Parse(_schemaAsset)
                }
            };
        }

        return body;
    }

    private static JsonArray BuildUserContent(List<JsonObject> imageParts)
    {
        var parts = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = UserInstruction
            }
        };

        foreach (var image in imageParts)
        {
            parts.Add(image);
        }

        return parts;
    }

    private string BuildPrompt(string mode)
    {
        if (mode == "prompt_only")
        {
            return string.Join('\n',
            [
                _promptAsset,
                string.Empty,
                "Return a JSON object with top-level key \"results\" only.",
                "Each result must include document_title, invoice_date, invoice_number, currency_code, gross_amount, seller, end_customer, and purchases.",
                "seller must include name, city, state, country.",
                "end_customer must include name, city, state, country, email.",
                "Each purchase must include quantity, part_number, description, serial_numbers, unit_price."
            ]);
        }

        return _promptAsset;
    }

    private static string ExtractMessageContent(JsonNode? payload)
    {
        var error = payload?["error"]?["message"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(error))
        {
            throw new AppException(502, "llama_upstream_error", error);
        }

        var message = payload?["choices"]?[0]?["message"];
        if (message is null)
        {
            throw new AppException(502, "llama_upstream_error", "vLLM response did not include a message.");
        }

        var content = message["content"];
        if (content is JsonValue)
        {
            return content.GetValue<string>();
        }

        if (content is JsonArray arr)
        {
            return string.Concat(arr
                .Where(part => part?["type"]?.GetValue<string>() == "text")
                .Select(part => part?["text"]?.GetValue<string>() ?? string.Empty)).Trim();
        }

        return message["reasoning_content"]?.GetValue<string>()?.Trim() ?? string.Empty;
    }

    private static JsonNode ParseStructuredContent(string value)
    {
        var normalized = StripJsonCodeFence(value);
        var candidates = new[] { normalized, ExtractBalancedJson(normalized) }
            .Where(c => !string.IsNullOrWhiteSpace(c));

        Exception? lastError = null;
        foreach (var candidate in candidates)
        {
            try
            {
                return JsonNode.Parse(candidate!) ?? throw new JsonException("Empty JSON payload.");
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new AppException(502, "invalid_model_output", "vLLM returned invalid JSON content.", new { cause = lastError?.Message, content = normalized });
    }

    private static string StripJsonCodeFence(string value)
    {
        var trimmed = value.Trim();
        var match = JsonFence().Match(trimmed);
        return (match.Success ? match.Groups[1].Value : trimmed).Trim();
    }

    private static string? ExtractBalancedJson(string value)
    {
        var start = value.IndexOfAny(['{', '[']);
        if (start < 0)
        {
            return null;
        }

        var opening = value[start];
        var closing = opening == '{' ? '}' : ']';
        var depth = 0;
        var inString = false;
        var escaping = false;

        for (var i = start; i < value.Length; i++)
        {
            var ch = value[i];
            if (escaping)
            {
                escaping = false;
                continue;
            }

            if (ch == '\\')
            {
                escaping = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (ch == opening)
            {
                depth++;
            }
            else if (ch == closing)
            {
                depth--;
                if (depth == 0)
                {
                    return value[start..(i + 1)].Trim();
                }
            }
        }

        return null;
    }

    [GeneratedRegex("^```(?:json)?\\s*([\\s\\S]*?)\\s*```$", RegexOptions.IgnoreCase)]
    private static partial Regex JsonFence();

    [GeneratedRegex("response_format|json_schema|grammar", RegexOptions.IgnoreCase)]
    private static partial Regex StructuredOutputError();
}
