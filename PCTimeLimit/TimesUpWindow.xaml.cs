using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace PCTimeLimit;

public partial class TimesUpWindow : Window
{
    private bool _allowClose;

    public TimesUpWindow()
    {
        InitializeComponent();
        PreventClosing();
        HookKeyboard();
    }

    private void PreventClosing()
    {
        Closing += (_, e) =>
        {
            if (!_allowClose)
            {
                e.Cancel = true;
            }
        };

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
        };
    }

    public void ForceClose()
    {
        _allowClose = true;
        Dispatcher.Invoke(Close);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ForceFocus();
    }

    private void ForceFocus()
    {
        Task.Run(() =>
        {
            while (!_allowClose)
            {
                Dispatcher.Invoke(() =>
                {
                    Topmost = true;
                    Activate();
                    Focus();
                });

                Thread.Sleep(500);
            }
        });
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private void HookKeyboard()
    {
        Task.Run(() =>
        {
            while (!_allowClose)
            {
                if ((GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0)
                {
                    Dispatcher.Invoke(SendEscapeKey);
                    Thread.Sleep(50);
                }

                Thread.Sleep(50);
            }
        });
    }

    private static void SendEscapeKey()
    {
        var inputDown = new INPUT
        {
            type = 1,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)KeyInterop.VirtualKeyFromKey(Key.Escape),
                    dwFlags = 0
                }
            }
        };

        var inputUp = inputDown;
        inputUp.U.ki.dwFlags = 0x0002;

        _ = SendInput(1, new[] { inputDown }, Marshal.SizeOf<INPUT>());
        _ = SendInput(1, new[] { inputUp }, Marshal.SizeOf<INPUT>());
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }
}
