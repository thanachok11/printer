using System.Text.Json;
using Microsoft.Extensions.Hosting.WindowsServices;
using W80PrintService.Models;
using W80PrintService.Services;

var options = new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
};

var builder = WebApplication.CreateBuilder(options);

builder.Host.UseWindowsService(o => o.ServiceName = "W80PrintService");


builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

var printLock = new SemaphoreSlim(1, 1);

app.MapGet("/health", () => Results.Text("ok"));

app.MapPost("/print/image", async (PrintImageRequest req, IConfiguration cfg) =>
{
    if (string.IsNullOrWhiteSpace(req.ImageBase64))
        return Results.BadRequest(new { ok = false, error = "imageBase64 is required" });

    // เลือก printer
    var defaultPrinter = cfg["Printer:DefaultName"];
    var printerName = string.IsNullOrWhiteSpace(req.PrinterName) ? defaultPrinter : req.PrinterName;

    if (string.IsNullOrWhiteSpace(printerName))
        return Results.BadRequest(new { ok = false, error = "printerName is required (or set Printer:DefaultName in appsettings)" });

    var docName = string.IsNullOrWhiteSpace(req.DocName) ? (cfg["Printer:DefaultDocName"] ?? "W80-RAW") : req.DocName;

    try
    {
        // decode base64 (รองรับ data url)
        var raw = req.ImageBase64!;
        var cleaned = raw.Contains("base64,", StringComparison.OrdinalIgnoreCase)
            ? raw.Split("base64,", 2, StringSplitOptions.None)[1]
            : raw;

        byte[] imgBuf;
        try { imgBuf = Convert.FromBase64String(cleaned.Trim()); }
        catch { return Results.BadRequest(new { ok = false, error = "invalid base64" }); }

        if (imgBuf.Length == 0)
            return Results.BadRequest(new { ok = false, error = "imageBase64 decoded to empty buffer" });

        // แปลงเป็น ESC/POS raster
        var raster = await EscPos.ImageToRasterGsV0(imgBuf, req.PaperWidth, req.Threshold);

        var payload = EscPos.Concat(
            EscPos.EscInit(),
            EscPos.EscAlign(1),
            raster,
            EscPos.EscFeed(3),
            req.Cut ? EscPos.EscCut() : Array.Empty<byte>(),
            EscPos.EscAlign(0)
        );

        // (optional) debug dump
        if (req.SaveDebug)
        {
            var outDir = Path.Combine(AppContext.BaseDirectory, "out");
            Directory.CreateDirectory(outDir);
            var outFile = Path.Combine(outDir, $"job_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.bin");
            await File.WriteAllBytesAsync(outFile, payload);
        }

        await printLock.WaitAsync();
        try
        {
            var ok = RawPrinterHelper.SendBytesToPrinter(printerName!, payload, docName!, out var err);
            if (!ok) return Results.Problem(detail: err, statusCode: 500, title: "print failed");
        }
        finally
        {
            printLock.Release();
        }

        return Results.Ok(new { ok = true, printerName, bytesLength = payload.Length });
    }
    catch (Exception e)
    {
        return Results.Problem(detail: e.ToString(), statusCode: 500, title: "print failed");
    }
});

app.Run();
