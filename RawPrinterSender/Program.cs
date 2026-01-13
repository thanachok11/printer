using System;
using System.IO;
using System.Runtime.InteropServices;

public class RawPrinterHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public class DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)] public string pDocName = "W80-RAW";
        [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile = null;
        [MarshalAs(UnmanagedType.LPStr)] public string pDataType = "RAW";
    }


    [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern bool StartDocPrinter(IntPtr hPrinter, int Level, [In] DOCINFOA pDocInfo);

    [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
    public static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
    public static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
    public static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true)]
    public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    public static bool SendBytesToPrinter(string printerName, byte[] bytes, out string error)
    {
        error = "";
        if (bytes == null || bytes.Length == 0)
        {
            error = "EMPTY_BYTES";
            return false;
        }

        if (!OpenPrinter(printerName, out IntPtr hPrinter, IntPtr.Zero))
        {
            error = $"OpenPrinter failed. Win32Error={Marshal.GetLastWin32Error()}";
            return false;
        }

        try
        {
            var di = new DOCINFOA();

            if (!StartDocPrinter(hPrinter, 1, di))
            {
                error = $"StartDocPrinter failed. Win32Error={Marshal.GetLastWin32Error()}";
                return false;
            }

            try
            {
                if (!StartPagePrinter(hPrinter))
                {
                    error = $"StartPagePrinter failed. Win32Error={Marshal.GetLastWin32Error()}";
                    return false;
                }

                try
                {
                    IntPtr unmanaged = Marshal.AllocCoTaskMem(bytes.Length);
                    Marshal.Copy(bytes, 0, unmanaged, bytes.Length);

                    bool ok = WritePrinter(hPrinter, unmanaged, bytes.Length, out int written);
                    Marshal.FreeCoTaskMem(unmanaged);

                    if (!ok)
                    {
                        error = $"WritePrinter failed. Win32Error={Marshal.GetLastWin32Error()}";
                        return false;
                    }

                    if (written != bytes.Length)
                    {
                        error = $"WritePrinter incomplete. written={written} expected={bytes.Length}";
                        return false;
                    }

                    return true;
                }
                finally
                {
                    EndPagePrinter(hPrinter);
                }
            }
            finally
            {
                EndDocPrinter(hPrinter);
            }
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
    }
}

public class Program
{
    // Usage:
    // RawPrinterSender.exe "W80" "C:\kiosk\job.bin"
    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: RawPrinterSender.exe <PrinterName> <FilePath>");
            return 2;
        }

        string printerName = args[0];
        string filePath = args[1];

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"File not found: {filePath}");
            return 3;
        }

        try
        {
            byte[] data = File.ReadAllBytes(filePath);

            bool ok = RawPrinterHelper.SendBytesToPrinter(printerName, data, out string err);
            if (!ok)
            {
                Console.Error.WriteLine($"Print failed: {err}");
                return 1;
            }

            Console.WriteLine("OK");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 9;
        }
    }
}
