using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Silhouette;

namespace UiProfiler;

internal class CorProfilerCallback : CorProfilerCallback4Base
{
    private static string? _overlayPath;

    private readonly BlockingCollection<string> _messages = new();
    private readonly ManualResetEventSlim _responsiveMutex = new(false);
    private readonly ConcurrentQueue<long> _keyTimestamps = new();

    private uint _mainThreadId;
    private long _pausesCount;
    private long _suspendUntilTicks;

    protected override HResult Initialize(int iCorProfilerInfoVersion)
    {
        var modulePath = NativeMethods.GetModulePath();

        if (modulePath == null)
        {
            return HResult.E_FAIL;
        }

        _overlayPath = Path.Combine(Path.GetDirectoryName(modulePath)!, "UiProfiler.Overlay.exe");

        SuperluminalPerf.Initialize();

        _mainThreadId = NativeMethods.GetCurrentThreadId();

        new Thread(SenderThread)
        {
            IsBackground = true,
            Name = "UI Profiler Thread"
        }.Start();

        new Thread(LowLevelHookThread)
        {
            IsBackground = true,
            Name = "UI Profiler LL Hook"
        }.Start();

        new Thread(InputThread)
        {
            IsBackground = true,
            Name = "UI Profiler Monitor"
        }.Start();

        new Thread(MonitoringThread)
        {
            IsBackground = true,
            Name = "UI Responsiveness Monitor"
        }.Start();

        return HResult.S_OK;
    }

    private void LowLevelHookThread()
    {
        var hook = NativeMethods.SetWindowsHookEx(NativeMethods.HookType.WH_KEYBOARD_LL, LowLevelHookProc, 0, 0);

        if (hook == IntPtr.Zero)
        {
            Logger.Log($"Failed to install WH_KEYBOARD_LL hook: {Marshal.GetLastWin32Error()}");
            return;
        }

        // Message pump to keep the LL hook alive
        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        return;

        IntPtr LowLevelHookProc(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                _keyTimestamps.Enqueue(Stopwatch.GetTimestamp());
            }

            return NativeMethods.CallNextHookEx(0, code, wParam, lParam);
        }
    }

    private void SetHook(int threadId)
    {
        NativeMethods.SetWindowsHookEx(NativeMethods.HookType.WH_KEYBOARD, HookProc, 0, threadId);

        IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var isProbeKey = (int)wParam == 0xFC;

                if (!isProbeKey)
                {                    
                    Interlocked.Exchange(ref _suspendUntilTicks, Environment.TickCount64 + 3000);
                }

                // Dequeue the LL timestamp and signal responsiveness
                if (_keyTimestamps.TryDequeue(out _))
                {
                    _responsiveMutex.Set();
                }
            }

            return NativeMethods.CallNextHookEx(0, code, wParam, lParam);
        }
    }

    private void MonitoringThread()
    {
        SuperluminalPerf.SetCurrentThreadName("UI Responsiveness Monitor");
        var color = new SuperluminalPerf.ProfilerColor(255, 0, 0);

        bool isResponsive = true;

        var stopwatch = Stopwatch.StartNew();

        SuperluminalPerf.EventMarker eventMarker = default;

        while (true)
        {
            if (_responsiveMutex.Wait(20))
            {
                _responsiveMutex.Reset();

                if (!isResponsive)
                {
                    isResponsive = true;
                    eventMarker.Dispose();
                    stopwatch.Stop();
                    _messages.Add($"true|{stopwatch.ElapsedMilliseconds}");
                }
            }
            else
            {
                if (isResponsive)
                {
                    // No pending keys = idle, not frozen
                    if (_keyTimestamps.IsEmpty)
                    {
                        continue;
                    }

                    isResponsive = false;

                    var index = Interlocked.Increment(ref _pausesCount);
                    eventMarker = SuperluminalPerf.BeginEvent("UI freeze", $"Freeze {index}", color);
                    stopwatch.Restart();
                    _messages.Add("false");

                    // Now wait indefinitely
                    _responsiveMutex.Wait();
                }
            }
        }
    }

    private void InputThread()
    {
        SetHook((int)_mainThreadId);

        try
        {
            var inputs = new NativeMethods.INPUT[2];
            var inputSize = Marshal.SizeOf<NativeMethods.INPUT>();

            while (true)
            {
                Thread.Sleep(10);

                if (Environment.TickCount64 < Interlocked.Read(ref _suspendUntilTicks))
                {
                    continue;
                }

                WaitForMainWindow();

                const ushort key = 0xFC; // VK_NONAME
                const uint type = 0x1; // INPUT_KEYBOARD

                inputs[0] = new()
                {
                    u = new() { ki = new() { wVk = key } },
                    type = type
                };

                inputs[1] = new()
                {
                    u = new() { ki = new() { wVk = key, dwFlags = 0x2 /* KEYEVENTF_KEYUP */ } },
                    type = type
                };

                _ = NativeMethods.SendInput(2, inputs, inputSize);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"InputThread failed: {ex}");
        }
    }

    private void SenderThread()
    {
        try
        {
            var pipeName = $"UIProfiler-{Guid.NewGuid()}";

            var startInfo = new ProcessStartInfo(_overlayPath!)
            {
                UseShellExecute = false,
                Arguments = $"{Environment.ProcessId} {pipeName}"
            };

            Process.Start(startInfo);

            using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            pipeClient.Connect();

            using var writer = new StreamWriter(pipeClient);
            writer.AutoFlush = true;

            foreach (var message in _messages.GetConsumingEnumerable())
            {
                writer.WriteLine(message);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"SenderThread failed: {ex}");
        }
    }

    private static void WaitForMainWindow()
    {
        while (true)
        {
            var foreground = NativeMethods.GetForegroundWindow();

            if (foreground != IntPtr.Zero)
            {
                NativeMethods.GetWindowThreadProcessId(foreground, out var pid);

                if (pid == Environment.ProcessId)
                {
                    return;
                }
            }

            Thread.Sleep(100);
        }
    }
}