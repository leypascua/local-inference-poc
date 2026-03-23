using invoice_extraction_api.Configuration;
using invoice_extraction_api.Contracts;
using invoice_extraction_api.Models;

namespace invoice_extraction_api.Services;

public sealed class ExtractionPipelineService(
    InvoiceExtractionOptions options,
    TempFileService tempFileService,
    FileLoaderService fileLoaderService,
    PdfRasterizerService pdfRasterizerService,
    ImagePreparationService imagePreparationService,
    OpenAiClient openAiClient,
    ExtractionNormalizer normalizer)
{
    public async Task<ExtractionResponse> RunAsync(string requestId, InvoiceExtractionRequest request, CancellationToken cancellationToken)
    {
        var requestDir = await tempFileService.CreateRequestTempDirAsync(requestId, cancellationToken);

        try
        {
            var loadedFiles = await fileLoaderService.LoadInputFilesAsync(requestDir, request.Content!, cancellationToken);
            var images = new List<PreparedImageFile>();

            foreach (var file in loadedFiles)
            {
                if (file.ContentType == "application/pdf")
                {
                    var rasterized = await pdfRasterizerService.RasterizePdfToImagesAsync(requestDir, file, cancellationToken);
                    images.AddRange(rasterized);
                }
                else
                {
                    images.Add(await imagePreparationService.PrepareUploadedImageAsync(requestDir, file, cancellationToken));
                }

                if (images.Count > options.MaxTotalImages)
                {
                    throw new AppException(413, "payload_too_large", "Request exceeds MAX_TOTAL_IMAGES after PDF rasterization.");
                }
            }

            var modelOutput = await openAiClient.ExtractAsync(options, images, cancellationToken);
            return normalizer.NormalizeAndValidate(modelOutput);
        }
        finally
        {
            if (!options.KeepDebugFiles)
            {
                await tempFileService.RemoveRequestTempDirAsync(requestDir, cancellationToken);
            }
        }
    }
}
