using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using invoice_extraction_api.Configuration;
using invoice_extraction_api.Contracts;
using invoice_extraction_api.Models;

namespace invoice_extraction_api.Services;

public sealed partial class FileLoaderService(IHttpClientFactory httpClientFactory, InvoiceExtractionOptions options)
{
    public async Task<List<LoadedSourceFile>> LoadInputFilesAsync(string requestDir, List<InvoiceContentItem> items, CancellationToken cancellationToken)
    {
        if (items.Count > options.MaxContentItems)
        {
            throw new AppException(413, "payload_too_large", $"content may not contain more than {options.MaxContentItems} item(s).");
        }

        var loaded = new List<LoadedSourceFile>();
        var seenNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var originalName = item.Name ?? "file";
            var storedName = FileNameService.ReserveUniqueFileName(FileNameService.SanitizeFileName(originalName), seenNames);
            var filePath = Path.Combine(requestDir, storedName);
            var bytes = await ResolveItemBytesAsync(item, cancellationToken);

            if (bytes.Length == 0)
            {
                throw new AppException(400, "empty_file", $"{originalName} did not contain any file bytes.");
            }

            if (bytes.Length > options.MaxFileBytes)
            {
                throw new AppException(413, "payload_too_large", $"{originalName} exceeds MAX_FILE_BYTES.");
            }

            totalBytes += bytes.Length;
            if (totalBytes > options.MaxTotalBytes)
            {
                throw new AppException(413, "payload_too_large", "Request exceeds MAX_TOTAL_BYTES.");
            }

            await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
            loaded.Add(new LoadedSourceFile
            {
                OriginalName = originalName,
                StoredName = storedName,
                FilePath = filePath,
                ContentType = item.ContentType!,
                ByteLength = bytes.Length
            });
        }

        return loaded;
    }

    private async Task<byte[]> ResolveItemBytesAsync(InvoiceContentItem item, CancellationToken cancellationToken)
    {
        if (IsRemoteUrl(item.FileUrl!))
        {
            return await FetchRemoteBytesAsync(item, cancellationToken);
        }

        return DecodeBase64(item.FileUrl!);
    }

    private async Task<byte[]> FetchRemoteBytesAsync(InvoiceContentItem item, CancellationToken cancellationToken)
    {
        if (!options.AllowRemoteUrls)
        {
            throw new AppException(400, "remote_urls_disabled", "Remote file_url values are disabled.");
        }

        var uri = new Uri(item.FileUrl!);
        ValidateRemoteUrl(uri);

        using var timeout = new CancellationTokenSource(options.RemoteFetchTimeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var client = httpClientFactory.CreateClient();
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(uri, linked.Token);
        }
        catch (Exception ex)
        {
            throw new AppException(502, "remote_fetch_failed", $"Unable to download {item.Name}.", new { cause = ex.Message });
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new AppException(502, "remote_fetch_failed", $"Unable to download {item.Name}.", new { status = (int)response.StatusCode });
        }

        if (response.Content.Headers.ContentLength is long contentLength && contentLength > options.MaxFileBytes)
        {
            throw new AppException(413, "payload_too_large", $"{item.Name} exceeds MAX_FILE_BYTES.");
        }

        return await response.Content.ReadAsByteArrayAsync(linked.Token);
    }

    private static bool IsRemoteUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static byte[] DecodeBase64(string input)
    {
        var normalized = DataUrlPrefix().Replace(input, string.Empty);
        normalized = Whitespace().Replace(normalized, string.Empty);

        if (string.IsNullOrWhiteSpace(normalized) || !Base64Chars().IsMatch(normalized))
        {
            throw new AppException(400, "invalid_base64", "file_url must be raw base64 bytes or an http(s) URL.");
        }

        try
        {
            return Convert.FromBase64String(normalized);
        }
        catch (FormatException)
        {
            throw new AppException(400, "invalid_base64", "file_url must be raw base64 bytes or an http(s) URL.");
        }
    }

    private static void ValidateRemoteUrl(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https"))
        {
            throw new AppException(400, "invalid_remote_url", "Remote file_url must use http or https.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new AppException(400, "invalid_remote_url", "Remote file_url must not include credentials.");
        }

        if (IsBlockedIpAddress(uri.Host))
        {
            throw new AppException(400, "blocked_remote_url", "Remote file_url points to a blocked host.");
        }
    }

    private static bool IsBlockedIpAddress(string hostname)
    {
        var lower = hostname.ToLowerInvariant();
        if (lower == "localhost" || lower.EndsWith(".local", StringComparison.Ordinal))
        {
            return true;
        }

        if (!IPAddress.TryParse(lower, out var ipAddress))
        {
            return false;
        }

        if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return lower == "::1"
                || lower.StartsWith("fc", StringComparison.Ordinal)
                || lower.StartsWith("fd", StringComparison.Ordinal)
                || lower.StartsWith("fe8", StringComparison.Ordinal)
                || lower.StartsWith("fe9", StringComparison.Ordinal)
                || lower.StartsWith("fea", StringComparison.Ordinal)
                || lower.StartsWith("feb", StringComparison.Ordinal);
        }

        var octets = lower.Split('.').Select(part => int.TryParse(part, out var n) ? n : -1).ToArray();
        var first = octets.ElementAtOrDefault(0);
        var second = octets.ElementAtOrDefault(1);

        return first == 0
            || first == 10
            || first == 127
            || (first == 169 && second == 254)
            || (first == 172 && second >= 16 && second <= 31)
            || (first == 192 && second == 168);
    }

    [GeneratedRegex("^data:[^;]+;base64,", RegexOptions.IgnoreCase)]
    private static partial Regex DataUrlPrefix();

    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("^[A-Za-z0-9+/=]+$")]
    private static partial Regex Base64Chars();
}
