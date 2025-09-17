using System.Runtime.InteropServices;

namespace UiProfiler;

internal unsafe class Logger
{  
    [DllImport("kernel32.dll")]
    private static extern void OutputDebugStringW(char* lpOutputString);
    internal static void Log(string message)
    {
        message = $"UI_PROFILER> {message}\r\n";

        fixed (char* messagePtr = message)
        {
            OutputDebugStringW(messagePtr);
        }
    }
}
