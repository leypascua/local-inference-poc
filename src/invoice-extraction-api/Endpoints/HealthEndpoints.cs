using invoice_extraction_api.Configuration;
using invoice_extraction_api.Models;
using invoice_extraction_api.Services;

namespace invoice_extraction_api.Endpoints;

public static class HealthEndpoints
{
    public static async Task<IResult> GetHealthAsync(
        TempFileService tempFileService,
        VllmClient vllmClient,
        InvoiceExtractionOptions options,
        CancellationToken cancellationToken)
    {
        await tempFileService.EnsureTempRootAsync(cancellationToken);
        var ready = await vllmClient.CheckReadinessAsync(options, cancellationToken);

        return Results.Json(new
        {
            status = ready ? "ready" : "degraded",
            checks = new
            {
                temp_dir = "ok",
                llama_cpp = ready ? "ok" : "unavailable"
            }
        }, statusCode: ready ? 200 : 503);
    }

    public static async Task<IResult> GetReadyAsync(
        TempFileService tempFileService,
        VllmClient vllmClient,
        InvoiceExtractionOptions options,
        CancellationToken cancellationToken)
    {
        await tempFileService.EnsureTempRootAsync(cancellationToken);
        var ready = await vllmClient.CheckReadinessAsync(options, cancellationToken);
        if (!ready)
        {
            throw new AppException(503, "service_unavailable", "vLLM is not ready.");
        }

        return Results.Json(new { status = "ready" });
    }
}
