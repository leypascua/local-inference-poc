using invoice_extraction_api.Models;

namespace invoice_extraction_api.Configuration;

public sealed class InvoiceExtractionOptions
{
    public int Port { get; init; } = 9998;
    public string Host { get; init; } = "0.0.0.0";
    public string TempDir { get; init; } = "/tmp/invoice-extraction";
    public string VllmBaseUrl { get; init; } = "http://localhost:8080";
    public string VllmModel { get; init; } = "zai-org/GLM-OCR";
    public bool AllowRemoteUrls { get; init; } = true;
    public bool KeepDebugFiles { get; init; } = true;
    public int RequestTimeoutMs { get; init; } = 120000;
    public int RemoteFetchTimeoutMs { get; init; } = 30000;
    public int MaxContentItems { get; init; } = 10;
    public int MaxFileBytes { get; init; } = 10 * 1024 * 1024;
    public int MaxTotalBytes { get; init; } = 50 * 1024 * 1024;
    public int MaxPdfPages { get; init; } = 20;
    public int MaxTotalImages { get; init; } = 40;
    public string LogLevel { get; init; } = "info";

    public static InvoiceExtractionOptions Load(IConfiguration config)
    {
        var vllmBaseUrl = ReadString(config, "VLLM_BASE_URL")
            ?? ReadString(config, "LLAMA_BASE_URL")
            ?? "http://localhost:8080";
        var vllmModel = ReadString(config, "VLLM_MODEL")
            ?? ReadString(config, "LLAMA_MODEL")
            ?? "zai-org/GLM-OCR";

        return new InvoiceExtractionOptions
        {
            Port = ParsePositiveInt(config["PORT"], 9998, "PORT"),
            Host = ReadString(config, "HOST") ?? "0.0.0.0",
            TempDir = ReadString(config, "TEMP_DIR") ?? "/tmp/invoice-extraction",
            VllmBaseUrl = vllmBaseUrl.TrimEnd('/'),
            VllmModel = vllmModel,
            AllowRemoteUrls = ParseBool(config["ALLOW_REMOTE_URLS"], true),
            KeepDebugFiles = ParseBool(config["KEEP_DEBUG_FILES"], true),
            RequestTimeoutMs = ParsePositiveInt(config["REQUEST_TIMEOUT_MS"], 120000, "REQUEST_TIMEOUT_MS"),
            RemoteFetchTimeoutMs = ParsePositiveInt(config["REMOTE_FETCH_TIMEOUT_MS"], 30000, "REMOTE_FETCH_TIMEOUT_MS"),
            MaxContentItems = ParsePositiveInt(config["MAX_CONTENT_ITEMS"], 10, "MAX_CONTENT_ITEMS"),
            MaxFileBytes = ParsePositiveInt(config["MAX_FILE_BYTES"], 10 * 1024 * 1024, "MAX_FILE_BYTES"),
            MaxTotalBytes = ParsePositiveInt(config["MAX_TOTAL_BYTES"], 50 * 1024 * 1024, "MAX_TOTAL_BYTES"),
            MaxPdfPages = ParsePositiveInt(config["MAX_PDF_PAGES"], 20, "MAX_PDF_PAGES"),
            MaxTotalImages = ParsePositiveInt(config["MAX_TOTAL_IMAGES"], 40, "MAX_TOTAL_IMAGES"),
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
