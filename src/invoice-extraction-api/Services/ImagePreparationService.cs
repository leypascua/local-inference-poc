using invoice_extraction_api.Configuration;
using invoice_extraction_api.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace invoice_extraction_api.Services;

public sealed class ImagePreparationService(InvoiceExtractionOptions options)
{
    public async Task<PreparedImageFile> PrepareUploadedImageAsync(string requestDir, LoadedSourceFile sourceFile, CancellationToken cancellationToken)
    {
        var outputPath = sourceFile.FilePath;
        var outputName = sourceFile.StoredName;

        await using var inputStream = File.OpenRead(sourceFile.FilePath);
        using var image = await Image.LoadAsync(inputStream, cancellationToken);

        if (!RequiresResize(image.Width, image.Height))
        {
            return new PreparedImageFile
            {
                FilePath = outputPath,
                FileName = outputName,
                ContentType = sourceFile.ContentType
            };
        }

        var resizedName = Path.ChangeExtension(sourceFile.StoredName, ".jpg");
        outputPath = Path.Combine(requestDir, resizedName);
        ResizeInPlace(image);
        await image.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = options.ImageJpegQuality }, cancellationToken);

        return new PreparedImageFile
        {
            FilePath = outputPath,
            FileName = resizedName,
            ContentType = "image/jpeg"
        };
    }

    public async Task<PreparedImageFile> SavePdfPageAsync(string requestDir, string fileName, Image image, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(requestDir, fileName);
        ResizeInPlace(image);
        await image.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = options.ImageJpegQuality }, cancellationToken);

        return new PreparedImageFile
        {
            FilePath = outputPath,
            FileName = fileName,
            ContentType = "image/jpeg"
        };
    }

    public async Task<(byte[] Bytes, string MimeType)> ReadForDataUrlAsync(PreparedImageFile imageFile, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(imageFile.FilePath, cancellationToken);
        return (bytes, imageFile.ContentType);
    }

    private bool RequiresResize(int width, int height)
    {
        if (options.ImageMaxDim == 0)
        {
            return false;
        }

        return Math.Max(width, height) > options.ImageMaxDim;
    }

    private void ResizeInPlace(Image image)
    {
        if (!RequiresResize(image.Width, image.Height))
        {
            return;
        }

        var scale = options.ImageMaxDim / (double)Math.Max(image.Width, image.Height);
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        image.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(width, height)
        }));
    }
}
