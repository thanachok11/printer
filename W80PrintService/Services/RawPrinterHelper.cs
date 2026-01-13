using System;
using System.Runtime.InteropServices;

namespace W80PrintService.Services;

public static class RawPrinterHelper
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

    public static bool SendBytesToPrinter(string printerName, byte[] bytes, string docName, out string error)
    {
        error = "";

        if (!OperatingSystem.IsWindows())
        {
            error = "RAW_PRINT_WINDOWS_ONLY";
            return false;
        }

        if (string.IsNullOrWhiteSpace(printerName))
        {
            error = "PRINTER_NAME_REQUIRED";
            return false;
        }

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
            var di = new DOCINFOA { pDocName = docName ?? "W80-RAW" };

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
                    try
                    {
                        Marshal.Copy(bytes, 0, unmanaged, bytes.Length);

                        bool ok = WritePrinter(hPrinter, unmanaged, bytes.Length, out int written);
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
                        Marshal.FreeCoTaskMem(unmanaged);
                    }
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
