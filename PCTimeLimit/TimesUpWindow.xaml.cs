using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WindowsInput;

namespace PCTimeLimit
{
    public partial class TimesUpWindow : Window
    {
        // for simulating inputs.
        private InputSimulator sim = new InputSimulator();

        public TimesUpWindow()
        {
            InitializeComponent();

            //PreventClosing();

            //HookKeyboard();
        }

        private void PreventClosing()
        {
            this.Closing += (s, e) =>
            {
                e.Cancel = true;
            };
            this.Loaded += (s, e) =>
            {
                this.Activate();
                this.Focus();
            };
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ForceFocus();
        }

        private void ForceFocus()
        {
            Task.Run(() =>
            {
                while (true)
                {
                    Dispatcher.Invoke(() =>
                    {
                        this.Topmost = true;
                        this.Activate();
                        this.Focus();
                    });
                    Thread.Sleep(500);
                }
            });
        }

        // P/Invoke for global keyboard hook
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);


        // For canceling windows key
        private void HookKeyboard()
        {
            Task.Run(() =>
            {
                while (true)
                {
                    // Check if the left or right Windows key is pressed
                    if ((GetAsyncKeyState(0x5B) & 0x8000) != 0 || // Left Windows Key (VK_LWIN)
                        (GetAsyncKeyState(0x5C) & 0x8000) != 0)   // Right Windows Key (VK_RWIN)
                    {
                        Dispatcher.Invoke(() => TypeWindowsText());
                        Thread.Sleep(50); // Prevent spamming
                    }

                    Thread.Sleep(50); // Reduce CPU usage
                }
            });
        }
        private void TypeWindowsText()
        {
            sim.Keyboard.KeyPress(WindowsInput.Native.VirtualKeyCode.ESCAPE); // Simulate typing "Windows"
        }
    }
}
