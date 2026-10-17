using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace FloorPlan.App;

static class NativeMethods
{
    const int AttachParentProcess = -1;
    const int StdOutputHandle = -11;
    const uint FileTypeUnknown = 0;
    const int DwmUseImmersiveDarkMode = 20;
    const int ShowNoActivate = 4;
    const uint NoSize = 0x0001, NoMove = 0x0002, NoActivate = 0x0010, ShowWindowFlag = 0x0040;
    static readonly IntPtr Topmost = new(-1);
    static readonly IntPtr NotTopmost = new(-2);
    const uint BreakawayFromJob = 0x01000000, Detached = 0x00000008, NewProcessGroup = 0x00000200;
    const int UseShowWindow = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInformation
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string? application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint flags, IntPtr environment, string? directory, ref StartupInfo startup, out ProcessInformation information);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

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

    /// <summary>Puts a window above the others, restored if minimised, leaving keyboard focus where the human is typing.</summary>
    public static void RaiseWithoutFocus(IntPtr window)
    {
        if (IsIconic(window)) ShowWindow(window, ShowNoActivate);
        const uint flags = NoMove | NoSize | NoActivate | ShowWindowFlag;
        SetWindowPos(window, Topmost, 0, 0, 0, 0, flags);
        SetWindowPos(window, NotTopmost, 0, 0, 0, 0, flags);
    }

    /// <summary>
    /// Starts a process that outlives this one: agent hosts may run their servers in a job that
    /// closes every child with it, so the new process leaves the job when the job allows that.
    /// </summary>
    public static bool StartDetached(string executable, IReadOnlyList<string> arguments, string directory)
    {
        var commandLine = string.Join(' ', new[] { executable }.Concat(arguments).Select(Quote));
        foreach (var flags in new[] { BreakawayFromJob | Detached | NewProcessGroup, Detached | NewProcessGroup })
        {
            // Its first window appears without taking the keyboard from the human.
            var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>(), dwFlags = UseShowWindow, wShowWindow = ShowNoActivate };
            if (CreateProcess(executable, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false, flags, IntPtr.Zero, directory, ref startup, out var started))
            {
                CloseHandle(started.hThread);
                CloseHandle(started.hProcess);
                return true;
            }
        }
        return false;
    }

    /// <summary>Quotes one argument for a Windows command line, as CommandLineToArgvW reads it back.</summary>
    static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0) return argument;
        var quoted = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes).Append(c);
            backslashes = 0;
        }
        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
