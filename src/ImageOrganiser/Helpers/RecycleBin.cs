using System.IO;
using System.Runtime.InteropServices;

namespace ImageOrganiser.Helpers;

/// <summary>Deletes a file, optionally sending it to the Recycle Bin via SHFileOperation.</summary>
public static class RecycleBin
{
    private const int FoDelete = 0x0003;
    private const int FofSilent = 0x0004;
    private const int FofNoConfirmation = 0x0010;
    private const int FofAllowUndo = 0x0040;
    private const int FofNoErrorUi = 0x0400;
    private const int FofWantNukeWarning = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public int wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct fileOp);

    public static void Delete(string path, bool toRecycleBin)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
        catch
        {
            // still try the delete
        }

        if (!toRecycleBin)
        {
            File.Delete(path);
            return;
        }

        var op = new ShFileOpStruct
        {
            hwnd = IntPtr.Zero,
            wFunc = FoDelete,
            pFrom = path + "\0\0",
            fFlags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi | FofWantNukeWarning
        };

        var result = SHFileOperation(ref op);
        if (result != 0 && File.Exists(path))
        {
            // Recycle failed (e.g. path too long, network share) — fall back to permanent delete.
            File.Delete(path);
        }
    }
}
