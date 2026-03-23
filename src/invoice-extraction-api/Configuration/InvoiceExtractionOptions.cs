using invoice_extraction_api.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace invoice_extraction_api.Configuration;

public sealed class InvoiceExtractionOptions
{
    public int Port { get; init; } = 9998;
    public string Host { get; init; } = "0.0.0.0";
    public string TempDir { get; init; } = "/tmp/invoice-extraction";
    public string OpenAiBaseUrl { get; init; } = "http://localhost:8080";
    public string OpenAiModel { get; init; } = "zai-org/GLM-OCR";
    public string? OpenAiApiKey { get; init; }
    public int OpenAiMaxTokens { get; init; } = 1024;
    public string OpenAiThinkingMode { get; init; } = "disabled";
    public string OpenAiReasoningEffort { get; init; } = "none";
    public bool AllowRemoteUrls { get; init; } = true;
    public bool KeepDebugFiles { get; init; } = true;
    public int RequestTimeoutMs { get; init; } = 120000;
    public int RemoteFetchTimeoutMs { get; init; } = 30000;
    public int MaxContentItems { get; init; } = 10;
    public long MaxFileSizeBytes { get; init; } = 10_000_000;
    public int MaxPdfPages { get; init; } = 20;
    public int MaxTotalImages { get; init; } = 40;
    public int ImageMaxDim { get; init; } = 1568;
    public int ImageJpegQuality { get; init; } = 85;
    public string LogLevel { get; init; } = "info";

    public static InvoiceExtractionOptions Load(IConfiguration config)
    {
        var openAiThinkingMode = ParseThinkingMode(ReadString(config, "OPENAI_THINKING_MODE") ?? "disabled");
        var openAiReasoningEffort = ParseReasoningEffort(ReadString(config, "OPENAI_REASONING_EFFORT") ?? "none");

        return new InvoiceExtractionOptions
        {
            Port = ParsePositiveInt(config["PORT"], 9998, "PORT"),
            Host = ReadString(config, "HOST") ?? "0.0.0.0",
            TempDir = ReadString(config, "TEMP_DIR") ?? "/tmp/invoice-extraction",
            OpenAiBaseUrl = (ReadString(config, "OPENAI_BASE_URL") ?? "http://localhost:8080").TrimEnd('/'),
            OpenAiModel = ReadString(config, "OPENAI_MODEL") ?? "zai-org/GLM-OCR",
            OpenAiApiKey = ReadString(config, "OPENAI_API_KEY"),
            OpenAiMaxTokens = ParsePositiveInt(config["OPENAI_MAX_TOKENS"], 1024, "OPENAI_MAX_TOKENS"),
            OpenAiThinkingMode = openAiThinkingMode,
            OpenAiReasoningEffort = openAiReasoningEffort,
            AllowRemoteUrls = ParseBool(config["ALLOW_REMOTE_URLS"], true),
            KeepDebugFiles = ParseBool(config["KEEP_DEBUG_FILES"], true),
            RequestTimeoutMs = ParsePositiveInt(config["REQUEST_TIMEOUT_MS"], 120000, "REQUEST_TIMEOUT_MS"),
            RemoteFetchTimeoutMs = ParsePositiveInt(config["REMOTE_FETCH_TIMEOUT_MS"], 30000, "REMOTE_FETCH_TIMEOUT_MS"),
            MaxContentItems = ParsePositiveInt(config["MAX_CONTENT_ITEMS"], 10, "MAX_CONTENT_ITEMS"),
            MaxFileSizeBytes = ParseSize(config["MAX_FILE_SIZE"], 10_000_000, "MAX_FILE_SIZE"),
            MaxPdfPages = ParsePositiveInt(config["MAX_PDF_PAGES"], 20, "MAX_PDF_PAGES"),
            MaxTotalImages = ParsePositiveInt(config["MAX_TOTAL_IMAGES"], 40, "MAX_TOTAL_IMAGES"),
            ImageMaxDim = ParseNonNegativeInt(config["IMAGE_MAX_DIM"], 1568, "IMAGE_MAX_DIM"),
            ImageJpegQuality = ParseRangeInt(config["IMAGE_JPEG_QUALITY"], 85, "IMAGE_JPEG_QUALITY", 1, 100),
            LogLevel = ReadString(config, "LOG_LEVEL") ?? "info"
        };
    }

    private static int ParsePositiveInt(string? value, int fallback, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (!int.TryParse(value, out var parsed) || parsed <= 0)
        {
            throw new AppException(500, "invalid_environment", $"{name} must be a positive integer.");
        }

        return parsed;
    }

    private static int ParseNonNegativeInt(string? value, int fallback, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (!int.TryParse(value, out var parsed) || parsed < 0)
        {
            throw new AppException(500, "invalid_environment", $"{name} must be a non-negative integer.");
        }

        return parsed;
    }

    private static int ParseRangeInt(string? value, int fallback, string name, int min, int max)
    {
        var parsed = ParsePositiveInt(value, fallback, name);
        if (parsed < min || parsed > max)
        {
            throw new AppException(500, "invalid_environment", $"{name} must be between {min} and {max}.");
        }

        return parsed;
    }

    private static string ParseThinkingMode(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is not ("disabled" or "enabled" or "auto"))
        {
            throw new AppException(500, "invalid_environment", "OPENAI_THINKING_MODE must be one of: disabled, enabled, auto.");
        }

        return normalized;
    }

    private static string ParseReasoningEffort(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is not ("none" or "low" or "medium" or "high"))
        {
            throw new AppException(500, "invalid_environment", "OPENAI_REASONING_EFFORT must be one of: none, low, medium, high.");
        }

        return normalized;
    }

    private static long ParseSize(string? value, long fallback, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var match = Regex.Match(value.Trim(), "^(?<number>\\d+(?:\\.\\d+)?)\\s*(?<unit>B|KB|MB|GB)$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            throw new AppException(500, "invalid_environment", $"{name} must be a human-readable size like 1024B, 1.44MB, or 2GB.");
        }

        if (!decimal.TryParse(match.Groups["number"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) || number < 0)
        {
            throw new AppException(500, "invalid_environment", $"{name} must be a valid non-negative size.");
        }

        var multiplier = match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "B" => 1m,
            "KB" => 1_000m,
            "MB" => 1_000_000m,
            "GB" => 1_000_000_000m,
            _ => throw new AppException(500, "invalid_environment", $"{name} has an unsupported size unit.")
        };

        var bytes = decimal.Round(number * multiplier, 0, MidpointRounding.AwayFromZero);
        if (bytes <= 0)
        {
            throw new AppException(500, "invalid_environment", $"{name} must be greater than zero.");
        }

        return (long)bytes;
    }

    private static bool ParseBool(string? value, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(IConfiguration config, string key)
    {
        var value = config[key];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
