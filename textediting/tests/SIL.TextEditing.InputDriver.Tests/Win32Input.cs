using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SIL.TextEditing.InputDriver.Tests
{
    /// <summary>The few user32 calls the driver needs: foreground handling and <c>SendInput</c>.</summary>
    static class Win32Input
    {
        const uint InputMouse = 0, InputKeyboard = 1;
        const uint KeyUp = 0x0002, KeyUnicode = 0x0004;
        const uint MouseLeftDown = 0x0002, MouseLeftUp = 0x0004;
        const ushort VkBack = 0x08, VkMenu = 0x12;
        const int SwRestore = 9;

        [StructLayout(LayoutKind.Sequential)]
        struct MouseInput { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        struct KeyboardInput { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion { [FieldOffset(0)] public MouseInput mi; [FieldOffset(0)] public KeyboardInput ki; }

        [StructLayout(LayoutKind.Sequential)]
        struct Input { public uint type; public InputUnion u; }

        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        // Screen coordinates from the host's snapshot are physical pixels, so this process must not be DPI-virtualised.
        static Win32Input() => SetProcessDPIAware();

        public static IntPtr ForegroundWindow => GetForegroundWindow();

        public static int ProcessOf(IntPtr hwnd)
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            return (int)pid;
        }

        /// <summary>Restores and activates the window, then waits until the foreground window belongs to <paramref name="pid"/>.</summary>
        public static bool BringToForeground(IntPtr hwnd, int pid, TimeSpan limit)
        {
            ShowWindow(hwnd, SwRestore);
            // A tap of Alt lets this process take the foreground when Windows would otherwise only flash the taskbar button.
            Send(Key(VkMenu, 0, 0), Key(VkMenu, 0, KeyUp));
            SetForegroundWindow(hwnd);
            var end = DateTime.UtcNow + limit;
            while (DateTime.UtcNow < end)
            {
                if (ProcessOf(GetForegroundWindow()) == pid) return true;
                System.Threading.Thread.Sleep(50);
            }
            return false;
        }

        public static uint Click(int x, int y)
        {
            SetCursorPos(x, y);
            return Send(Mouse(MouseLeftDown), Mouse(MouseLeftUp));
        }

        /// <summary>Each UTF-16 code unit as a down/up pair, as the Windows input path delivers Unicode text; a surrogate pair is two units.</summary>
        public static uint TypeUnicode(string text)
        {
            var events = new List<Input>();
            foreach (char c in text)
            {
                events.Add(Key(0, c, KeyUnicode));
                events.Add(Key(0, c, KeyUnicode | KeyUp));
            }
            return Send(events.ToArray());
        }

        public static uint Backspace(int count)
        {
            var events = new List<Input>();
            for (int i = 0; i < count; i++)
            {
                events.Add(Key(VkBack, 0, 0));
                events.Add(Key(VkBack, 0, KeyUp));
            }
            return Send(events.ToArray());
        }

        /// <summary>How a legacy-transport Keyman output arrives: <paramref name="deletes"/> Backspace presses, then the replacement as Unicode packets.</summary>
        public static uint KeymanLegacyOutput(int deletes, string replacement) => Backspace(deletes) + TypeUnicode(replacement);

        static Input Key(ushort vk, ushort scan, uint flags) =>
            new Input { type = InputKeyboard, u = new InputUnion { ki = new KeyboardInput { wVk = vk, wScan = scan, dwFlags = flags } } };

        static Input Mouse(uint flags) =>
            new Input { type = InputMouse, u = new InputUnion { mi = new MouseInput { dwFlags = flags } } };

        static uint Send(params Input[] events) => SendInput((uint)events.Length, events, Marshal.SizeOf<Input>());
    }
}
