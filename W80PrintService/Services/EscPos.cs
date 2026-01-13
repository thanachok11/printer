using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace W80PrintService.Services;

public static class EscPos
{
    private const byte ESC = 0x1B;
    private const byte GS = 0x1D;

    public static byte[] EscInit() => [ESC, 0x40];                 // ESC @
    public static byte[] EscAlign(byte align) => [ESC, 0x61, align]; // ESC a n
    public static byte[] EscFeed(byte n = 3) => [ESC, 0x64, n];     // ESC d n
    public static byte[] EscCut() => [GS, 0x56, 0x00];              // GS V 0

    /// <summary>
    /// Convert image buffer to ESC/POS raster command (GS v 0)
    /// - targetWidth: 576px (80mm @203dpi commonly)
    /// - threshold: 0..255 (ยิ่งต่ำยิ่งดำ)
    /// </summary>
    public static async Task<byte[]> ImageToRasterGsV0(byte[] imageBytes, int targetWidth = 576, int threshold = 180)
    {
        using var ms = new MemoryStream(imageBytes);
        using var img = await Image.LoadAsync<Rgba32>(ms);

        // resize (width only), keep aspect ratio
        if (img.Width > targetWidth)
        {
            img.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(targetWidth, 0)
            }));
        }

        // Convert -> grayscale luminance + threshold + pack bits
        int width = img.Width;
        int height = img.Height;

        int widthBytes = (int)Math.Ceiling(width / 8.0);
        byte xL = (byte)(widthBytes & 0xFF);
        byte xH = (byte)((widthBytes >> 8) & 0xFF);
        byte yL = (byte)(height & 0xFF);
        byte yH = (byte)((height >> 8) & 0xFF);

        // GS v 0 m xL xH yL yH  (m=0)
        var header = new byte[] { GS, 0x76, 0x30, 0x00, xL, xH, yL, yH };
        var raster = new byte[widthBytes * height];

        // อ่าน pixel row-by-row
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int xByte = 0; xByte < widthBytes; xByte++)
                {
                    byte b = 0;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        int x = xByte * 8 + bit;
                        if (x >= width) continue;

                        Rgba32 p = row[x];

                        // luminance (0..255)
                        int lum = (int)Math.Round(0.299 * p.R + 0.587 * p.G + 0.114 * p.B);

                        // threshold: black if lum < threshold
                        if (lum < threshold)
                        {
                            b |= (byte)(0x80 >> bit);
                        }
                    }
                    raster[y * widthBytes + xByte] = b;
                }
            }
        });

        // concat header + raster
        var outBuf = new byte[header.Length + raster.Length];
        Buffer.BlockCopy(header, 0, outBuf, 0, header.Length);
        Buffer.BlockCopy(raster, 0, outBuf, header.Length, raster.Length);
        return outBuf;
    }

    public static byte[] Concat(params byte[][] parts)
    {
        int len = 0;
        foreach (var p in parts) len += p.Length;

        var result = new byte[len];
        int offset = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, result, offset, p.Length);
            offset += p.Length;
        }
        return result;
    }
}
