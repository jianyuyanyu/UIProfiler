using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace UiProfiler.Overlay;

public partial class App : Application
{
    private Process? _parentProcess;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length < 2)
        {
            MessageBox.Show("Usage: <pid> <pipe_name>", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var pid = int.Parse(e.Args[0]);
        var pipeName = e.Args[1];

        _parentProcess = Process.GetProcessById(pid);
        _parentProcess.Exited += (_, _) => Dispatcher.Invoke(Shutdown);
        _parentProcess.EnableRaisingEvents = true;

        var overlay = LoadOverlay();

        Task.Run(() =>
        {
            var window = FindVsWindow(pid);
            Dispatcher.BeginInvoke(() => overlay.Show(window));
        });

        new Thread(() => Listener(pipeName, overlay))
        {
            IsBackground = true,
            Name = "Listener Thread"
        }.Start();
    }

    private IntPtr FindVsWindow(int pid)
    {
        //bool leftSide = false;

        while (true)
        {
            var mainWindow = FindWindowWithCaption((uint)pid, "Visual Studio Preview");

            if (mainWindow != IntPtr.Zero)
            {
                //// Move the window to the left half of the screen
                //// Get screen dimensions
                //int screenWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
                //int screenHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);

                //// Calculate dimensions for the left half of the screen
                //int newWidth = screenWidth / 2;
                //int newHeight = screenHeight; // Occupy full height

                //int newX = leftSide ? 0 : screenWidth / 2; 
                //int newY = 0; // Starting from the top edge

                //// Resize and move the window to the left half of the screen
                //// SWP_SHOWWINDOW ensures the window is visible
                //NativeMethods.SetWindowPos(mainWindow, IntPtr.Zero, newX, newY, newWidth, newHeight, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

                //// Move the mouse cursor to the center of the Visual Studio window (now resized)
                //NativeMethods.GetWindowRect(mainWindow, out NativeMethods.RECT rect);
                //// Re-calculate center based on the new dimensions
                //int centerX = rect.Left + (rect.Right - rect.Left) / 2;
                //int centerY = rect.Top + (rect.Bottom - rect.Top) / 2;

                //// Move the cursor
                //NativeMethods.SetCursorPos(centerX, centerY);

                //Dispatcher.BeginInvoke(() => ShowOverlay(mainWindow));

                //return mainWindow;

                // Move the mouse cursor to the center of the Visual Studio window
                NativeMethods.GetWindowRect(mainWindow, out NativeMethods.RECT rect);
                int centerX = (rect.Left + rect.Right) / 3;
                int centerY = (rect.Top + rect.Bottom) / 3;
                // Move the cursor
                NativeMethods.SetCursorPos(centerX, centerY);

                return mainWindow;
            }

            Thread.Sleep(100);
        }
    }

    private static IntPtr FindWindowWithCaption(uint processId, string windowCaption)
    {
        IntPtr foundWindow = IntPtr.Zero;

        // Enumerate all top-level windows
        NativeMethods.EnumWindows(EnumWindowsCallback, IntPtr.Zero);

        return foundWindow;

        bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam)
        {
            // First, filter by process ID
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint windowProcessId);
            if (windowProcessId != processId)
            {
                return true; // Skip this window (continue enumeration)
            }

            // Get the window's title/caption
            var windowTitle = new StringBuilder(256);
            NativeMethods.GetWindowText(hWnd, windowTitle, 256);

            // Check if the window is visible and matches the desired caption
            if (NativeMethods.IsWindowVisible(hWnd) && windowTitle.ToString().Contains(windowCaption, StringComparison.OrdinalIgnoreCase))
            {
                foundWindow = hWnd; // We found the window
                return false;       // Stop enumeration
            }

            return true; // Continue enumeration
        }
    }

    private void Listener(string pipeName, OverlayWindow overlay)
    {
        try
        {
            using var pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.InOut);
            pipeServer.WaitForConnection();

            using var reader = new StreamReader(pipeServer);

            while (true)
            {
                var message = reader.ReadLine();

                if (message == null)
                {
                    break;
                }

                var values = message.Split('|');

                if (values[0] == "true")
                {
                    Dispatcher.BeginInvoke(() => overlay.UpdateResponsiveness(true, long.Parse(values[1])));
                }
                else if (values[0] == "false")
                {
                    Dispatcher.BeginInvoke(() => overlay.UpdateResponsiveness(false, 0));
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error in pipe listener: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static OverlayWindow LoadOverlay()
    {
        var overlay = new OverlayWindow
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true
        };

        overlay.SourceInitialized += (s, e) =>
        {
            var handle = new WindowInteropHelper(overlay).Handle;
            var extendedStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);

            // Add WS_EX_TRANSPARENT style to allow clicks to pass through
            _ = NativeMethods.SetWindowLong(
                handle,
                NativeMethods.GWL_EXSTYLE,
                extendedStyle | NativeMethods.WS_EX_TRANSPARENT);
        };

        return overlay;
    }
}

