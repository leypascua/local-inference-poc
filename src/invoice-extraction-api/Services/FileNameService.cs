using System.Text.RegularExpressions;

namespace invoice_extraction_api.Services;

public static partial class FileNameService
{
    private const string SafeFileNameFallback = "file";

    public static string SanitizeFileName(string input)
    {
        var baseName = Path.GetFileName(input);
        var cleaned = baseName;
        cleaned = ControlChars().Replace(cleaned, string.Empty);
        cleaned = cleaned.Replace("\\", "_").Replace("/", "_");
        cleaned = Whitespace().Replace(cleaned, "_");
        cleaned = UnsafeChars().Replace(cleaned, "_");
        cleaned = MultiUnderscore().Replace(cleaned, "_");
        cleaned = LeadingDots().Replace(cleaned, string.Empty);
        cleaned = TrimUnderscore().Replace(cleaned, string.Empty);

        return string.IsNullOrWhiteSpace(cleaned) ? SafeFileNameFallback : cleaned;
    }

    public static string ReserveUniqueFileName(string fileName, Dictionary<string, int> seen)
    {
        var normalized = fileName.ToLowerInvariant();
        if (!seen.TryGetValue(normalized, out var currentCount))
        {
            seen[normalized] = 1;
            return fileName;
        }

        var parsedName = Path.GetFileNameWithoutExtension(fileName);
        var parsedExt = Path.GetExtension(fileName);

        var nextCount = currentCount + 1;
        var candidate = $"{parsedName}_{nextCount}{parsedExt}";
        while (seen.ContainsKey(candidate.ToLowerInvariant()))
        {
            nextCount += 1;
            candidate = $"{parsedName}_{nextCount}{parsedExt}";
        }

        seen[normalized] = nextCount;
        seen[candidate.ToLowerInvariant()] = 1;
        return candidate;
    }

    public static string BuildPdfPageFileName(string pdfFileName, int pageNumber)
    {
        return $"{pdfFileName}.page-{pageNumber:000}.jpg";
    }

    [GeneratedRegex("[\\u0000-\\u001f\\u007f]+")]
    private static partial Regex ControlChars();

    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex UnsafeChars();

    [GeneratedRegex("_+")]
    private static partial Regex MultiUnderscore();

    [GeneratedRegex("^\\.+")]
    private static partial Regex LeadingDots();

    [GeneratedRegex("^_+|_+$")]
    private static partial Regex TrimUnderscore();
}
