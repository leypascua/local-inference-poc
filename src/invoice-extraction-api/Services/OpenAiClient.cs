using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using invoice_extraction_api.Configuration;
using invoice_extraction_api.Models;

namespace invoice_extraction_api.Services;

public sealed class OpenAiClient(IHttpClientFactory httpClientFactory, ImagePreparationService imagePreparationService)
{
    private const string UserInstruction = "Extract the uploaded documents into one JSON object that matches the schema exactly.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _promptAsset = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "invoice-extraction-prompt.md")).Trim();
    private readonly JsonObject? _structuredResponseSchema = ParseStructuredResponseSchema(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "invoice-extraction-output-schema.json")));

    public async Task<JsonNode> ExtractAsync(InvoiceExtractionOptions options, List<PreparedImageFile> images, CancellationToken cancellationToken)
    {
        var imageParts = new JsonArray();
        foreach (var image in images)
        {
            var (bytes, mimeType) = await imagePreparationService.ReadForDataUrlAsync(image, cancellationToken);
            var base64Payload = Convert.ToBase64String(bytes);
            imageParts.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject { ["url"] = $"data:{mimeType};base64,{base64Payload}" }
            });
        }

        var body = BuildRequestBody(options, imageParts);
        using var response = await CreateChatCompletionAsync(options, body, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new AppException(502, "upstream_api_error", $"OpenAI-compatible upstream returned HTTP {(int)response.StatusCode}.", string.IsNullOrWhiteSpace(errorBody) ? null : new { body = errorBody });
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonNode>(JsonOptions, cancellationToken);
        var content = ExtractMessageContent(payload);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new AppException(502, "invalid_model_output", "OpenAI-compatible upstream returned empty completion content.");
        }

        try
        {
            return JsonNode.Parse(content) ?? throw new JsonException("Empty JSON payload.");
        }
        catch (Exception ex)
        {
            throw new AppException(502, "invalid_model_output", "OpenAI-compatible upstream returned invalid JSON content.", new { cause = ex.Message, content });
        }
    }

    public async Task<bool> CheckReadinessAsync(InvoiceExtractionOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            ConfigureAuthorization(client, options);
            using var timeout = new CancellationTokenSource(Math.Min(options.RemoteFetchTimeoutMs, 5000));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var response = await client.GetAsync($"{options.OpenAiBaseUrl}/v1/models", linked.Token);
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
        ConfigureAuthorization(client, options);
        using var timeout = new CancellationTokenSource(options.RequestTimeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{options.OpenAiBaseUrl}/v1/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        try
        {
            return await client.SendAsync(request, linked.Token);
        }
        catch (Exception ex)
        {
            throw new AppException(502, "upstream_api_error", "Unable to reach OpenAI-compatible upstream.", new { cause = ex.Message });
        }
    }

    private JsonObject BuildRequestBody(InvoiceExtractionOptions options, JsonArray imageParts)
    {
        var body = new JsonObject
        {
            ["model"] = options.OpenAiModel,
            ["temperature"] = 0,
            ["max_tokens"] = options.OpenAiMaxTokens,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = _promptAsset
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = BuildUserContent(imageParts)
                }
            }
        };

        if (_structuredResponseSchema is not null)
        {
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = "invoice_extraction_response",
                    ["strict"] = true,
                    ["schema"] = _structuredResponseSchema.DeepClone()
                }
            };
        }

        if (options.OpenAiThinkingMode == "disabled")
        {
            body["reasoning_effort"] = "none";
        }
        else if (options.OpenAiThinkingMode == "enabled")
        {
            body["reasoning_effort"] = options.OpenAiReasoningEffort;
        }

        return body;
    }

    private static JsonArray BuildUserContent(JsonArray imageParts)
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
            parts.Add(image!.DeepClone());
        }

        return parts;
    }

    private static JsonObject? ParseStructuredResponseSchema(string? schemaAsset)
    {
        if (string.IsNullOrWhiteSpace(schemaAsset))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(schemaAsset) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ExtractMessageContent(JsonNode? payload)
    {
        var error = payload?["error"]?["message"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(error))
        {
            throw new AppException(502, "upstream_api_error", error);
        }

        var message = payload?["choices"]?[0]?["message"];
        if (message is null)
        {
            throw new AppException(502, "upstream_api_error", "OpenAI-compatible response did not include a message.");
        }

        var content = message["content"];
        if (content is JsonValue)
        {
            return content.GetValue<string>().Trim();
        }

        if (content is JsonArray arr)
        {
            return string.Concat(arr
                .Where(part => part?["type"]?.GetValue<string>() == "text")
                .Select(part => part?["text"]?.GetValue<string>() ?? string.Empty)).Trim();
        }

        return string.Empty;
    }

    private static void ConfigureAuthorization(HttpClient client, InvoiceExtractionOptions options)
    {
        client.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(options.OpenAiApiKey)
            ? null
            : new AuthenticationHeaderValue("Bearer", options.OpenAiApiKey);
    }
}
