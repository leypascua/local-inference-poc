using Docnet.Core;
using Docnet.Core.Models;
using invoice_extraction_api.Configuration;
using invoice_extraction_api.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace invoice_extraction_api.Services;

public sealed class PdfRasterizerService(InvoiceExtractionOptions options, ImagePreparationService imagePreparationService)
{
    public async Task<List<PreparedImageFile>> RasterizePdfToImagesAsync(string requestDir, LoadedSourceFile sourceFile, CancellationToken cancellationToken)
    {
        try
        {
            var images = new List<PreparedImageFile>();
            using var docReader = DocLib.Instance.GetDocReader(sourceFile.FilePath, new PageDimensions(2000, 2000));
            var pageCount = docReader.GetPageCount();
            if (pageCount > options.MaxPdfPages)
            {
                throw new AppException(413, "payload_too_large", $"{sourceFile.OriginalName} exceeds MAX_PDF_PAGES.");
            }

            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var pageReader = docReader.GetPageReader(pageIndex);
                var rawBytes = pageReader.GetImage();
                var width = pageReader.GetPageWidth();
                var height = pageReader.GetPageHeight();

                using var image = Image.LoadPixelData<Bgra32>(rawBytes, width, height);
                var fileName = FileNameService.BuildPdfPageFileName(sourceFile.StoredName, pageIndex + 1);
                images.Add(await imagePreparationService.SavePdfPageAsync(requestDir, fileName, image, cancellationToken));
            }

            return images;
        }
        catch (AppException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AppException(422, "pdf_rasterization_failed", $"Unable to rasterize PDF {sourceFile.OriginalName}.", new { cause = ex.Message });
        }
    }
}
