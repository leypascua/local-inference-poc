namespace invoice_extraction_api.Models;

public sealed class AppException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }
    public object? Details { get; }

    public AppException(int statusCode, string code, string message, object? details = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Details = details;
    }
}

public sealed class LoadedSourceFile
{
    public required string OriginalName { get; init; }
    public required string StoredName { get; init; }
    public required string FilePath { get; init; }
    public required string ContentType { get; init; }
    public required int ByteLength { get; init; }
}

public sealed class PreparedImageFile
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
}
