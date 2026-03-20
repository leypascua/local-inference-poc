using invoice_extraction_api.Configuration;
using invoice_extraction_api.Contracts;
using invoice_extraction_api.Endpoints;
using invoice_extraction_api.Middleware;
using invoice_extraction_api.Models;
using invoice_extraction_api.Services;
using Microsoft.AspNetCore.Http.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(InvoiceExtractionOptions.Load(builder.Configuration));
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddHttpClient();
builder.Services.AddSingleton<TempFileService>();
builder.Services.AddSingleton<FileLoaderService>();
builder.Services.AddSingleton<PdfRasterizerService>();
builder.Services.AddSingleton<VllmClient>();
builder.Services.AddSingleton<ExtractionNormalizer>();
builder.Services.AddSingleton<ExtractionPipelineService>();

var app = builder.Build();

app.UseMiddleware<RequestIdMiddleware>();
app.UseMiddleware<ErrorHandlingMiddleware>();

app.MapGet("/health/live", () => Results.Json(new { status = "ok" }));
app.MapGet("/health", HealthEndpoints.GetHealthAsync);
app.MapGet("/health/ready", HealthEndpoints.GetReadyAsync);

app.MapPost("/invoices/extract/", InvoiceEndpoints.ExtractAsync);

app.MapFallback((HttpContext context) =>
{
    var requestId = context.GetRequestId();
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    return context.Response.WriteAsJsonAsync(new ErrorEnvelope(
        requestId,
        new ErrorBody("not_found", "Route not found.")
    ));
});

app.Run();
