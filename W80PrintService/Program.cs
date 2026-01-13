using System.Text.Json;
using W80PrintService.Models;
using W80PrintService.Services;

var builder = WebApplication.CreateBuilder(args);

// JSON body size limit ~20MB
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});


var app = builder.Build();

app.MapGet("/health", () => Results.Text("ok"));

app.MapPost("/print/image", async (PrintImageRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ImageBase64))
        return Results.BadRequest(new { ok = false, error = "imageBase64 is required" });

    try
    {
        // รองรับ data url
        var raw = req.ImageBase64!;
        var cleaned = raw.Contains("base64,", StringComparison.OrdinalIgnoreCase)
            ? raw.Split("base64,", 2, StringSplitOptions.None)[1]
            : raw;

        var trimmed = cleaned.Trim();

        byte[] imgBuf;
        try
        {
            imgBuf = Convert.FromBase64String(trimmed);
        }
        catch
        {
            return Results.BadRequest(new { ok = false, error = "invalid base64" });
        }

        if (imgBuf.Length == 0)
            return Results.BadRequest(new { ok = false, error = "imageBase64 decoded to empty buffer (check base64 content)" });

        var raster = await EscPos.ImageToRasterGsV0(imgBuf, req.PaperWidth, req.Threshold);

        var payload = EscPos.Concat(
            EscPos.EscInit(),
            EscPos.EscAlign(1),
            raster,
            EscPos.EscFeed(3),
            req.Cut ? EscPos.EscCut() : Array.Empty<byte>(),
            EscPos.EscAlign(0)
        );

        if (req.SaveDebug)
        {
            var outDir = Path.Combine(Directory.GetCurrentDirectory(), "out");
            Directory.CreateDirectory(outDir);
            var outFile = Path.Combine(outDir, $"job_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.bin");
            await File.WriteAllBytesAsync(outFile, payload);
        }

        return Results.Ok(new
        {
            ok = true,
            bytesLength = payload.Length,
            escposBase64 = Convert.ToBase64String(payload)
        });
    }
    catch (Exception e)
    {
        return Results.Problem(detail: e.ToString(), statusCode: 500, title: "print failed");
    }
});

app.Run();
