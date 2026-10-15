using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FloorPlan.App;

static class NativeMethods
{
    const int AttachParentProcess = -1;
    const int StdOutputHandle = -11;
    const uint FileTypeUnknown = 0;
    const int DwmUseImmersiveDarkMode = 20;

    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll")]
    static extern uint GetFileType(IntPtr handle);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>A windowed executable has no console; borrow the caller's unless output is redirected.</summary>
    public static void AttachParentConsoleIfNeeded()
    {
        if (GetFileType(GetStdHandle(StdOutputHandle)) == FileTypeUnknown) AttachConsole(AttachParentProcess);
    }

    public static void UseDarkTitleBar(IntPtr window, bool dark)
    {
        int value = dark ? 1 : 0;
        DwmSetWindowAttribute(window, DwmUseImmersiveDarkMode, ref value, sizeof(int));
    }

    public static bool AppsUseDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }
}
