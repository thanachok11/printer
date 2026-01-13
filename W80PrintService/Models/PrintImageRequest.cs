namespace W80PrintService.Models;

public sealed class PrintImageRequest
{
    public string? ImageBase64 { get; set; }
    public int PaperWidth { get; set; } = 576;
    public int Threshold { get; set; } = 180;
    public bool Cut { get; set; } = true;
    public bool SaveDebug { get; set; } = false;
}
