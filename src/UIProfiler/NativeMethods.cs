using System.Runtime.InteropServices;

namespace UiProfiler;

internal static unsafe partial class NativeMethods
{
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint SuspendThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenThread(int dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    internal static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        UIntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult
    );

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(
        uint nInputs,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] INPUT[] pInputs,
        int cbSize
    );

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    internal static extern bool PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    internal static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(NativeMethods.HookType hookType, HookProc lpfn, IntPtr hMod, int dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string lpModuleName);

    internal static string? GetModulePath()
    {
        var addr = (IntPtr)(delegate*<string?>)&GetModulePath;

        var flags = 0x4 /* GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS */ | 0x2 /* GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT */;
        if (GetModuleHandleExW(flags, addr, out var hModule) == 0)
        {
            return null;
        }

        const int bufferSize = 1024;
        var buffer = stackalloc char[bufferSize];
        var length = GetModuleFileNameW(hModule, buffer, bufferSize);
        return length > 0 ? new string(buffer, 0, length) : null;
    }

    [LibraryImport("kernel32", SetLastError = true)]
    private static partial int GetModuleHandleExW(int dwFlags, IntPtr lpModuleName, out IntPtr phModule);

    [LibraryImport("kernel32", SetLastError = true)]
    private static partial int GetModuleFileNameW(IntPtr hModule, char* lpFilename, int nSize);




    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    internal enum HookType : int
    {
        WH_CALLWNDPROC = 4,
        WH_GETMESSAGE = 3,
        WH_MOUSE = 7,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CWPSTRUCT
    {
        public IntPtr lParam;
        public IntPtr wParam;
        public uint message;
        public IntPtr hwnd;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEHOOKSTRUCT
    {
        public POINT pt;
        public IntPtr hwnd;
        public uint hitTestCode;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetTimer(
        IntPtr hWnd,
        IntPtr nIDEvent,
        uint uElapse,
        IntPtr lpTimerFunc
    );
}
