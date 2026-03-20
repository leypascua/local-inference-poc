using invoice_extraction_api.Configuration;

namespace invoice_extraction_api.Services;

public sealed class TempFileService
{
    public Task EnsureTempRootAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tempDir = _options.TempDir;
        Directory.CreateDirectory(tempDir);

        var probePath = Path.Combine(tempDir, $".probe-{Guid.NewGuid():N}");
        File.WriteAllText(probePath, "ok");
        File.Delete(probePath);
        return Task.CompletedTask;
    }

    public async Task<string> CreateRequestTempDirAsync(string requestId, CancellationToken cancellationToken)
    {
        await EnsureTempRootAsync(cancellationToken);
        var requestDir = Path.Combine(_options.TempDir, requestId);
        Directory.CreateDirectory(requestDir);
        return requestDir;
    }

    public Task RemoveRequestTempDirAsync(string requestDir, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(requestDir))
        {
            Directory.Delete(requestDir, recursive: true);
        }

        return Task.CompletedTask;
    }

    public TempFileService(InvoiceExtractionOptions options)
    {
        _options = options;
    }

    private readonly InvoiceExtractionOptions _options;
}
