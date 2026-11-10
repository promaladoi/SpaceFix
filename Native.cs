using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LayoutPilot
{
    struct Field
    {
        public IntPtr Window, Focus, Layout;

        public bool SameField(Field other)
        {
            return Focus != IntPtr.Zero && Window == other.Window && Focus == other.Focus;
        }

        public bool Matches(Field other) { return SameField(other) && Layout == other.Layout; }
    }

    static class Native
    {
        public const int KeyDown = 0x100, KeyUp = 0x101, SystemKeyDown = 0x104, SystemKeyUp = 0x105;
        public const int Backspace = 0x08, Space = 0x20, F10 = 0x79;
        private const uint KeyUpFlag = 2, UnicodeFlag = 4;
        private const uint EditGetSelection = 0xb0, WindowGetText = 0x0d;
        public static readonly UIntPtr OwnInput = new UIntPtr(0x4c59504c);

        public delegate IntPtr Hook(int code, IntPtr message, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        public struct KeyEvent
        {
            public uint Key, Scan, Flags, Time;
            public UIntPtr Extra;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MouseEvent
        {
            public Point Position;
            public uint Data, Flags, Time;
            public UIntPtr Extra;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GuiInfo
        {
            public int Size, Flags;
            public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort Key, Scan;
            public uint Flags, Time;
            public UIntPtr Extra;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputData
        {
            [FieldOffset(0)] public KeyboardInput Keyboard;
            [FieldOffset(0)] public MouseEvent Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint Type;
            public InputData Data;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int type, Hook procedure, IntPtr module, uint thread);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr process);
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
        [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll")] public static extern int GetKeyboardLayoutList(int count, [Out] IntPtr[] layouts);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern short GetKeyState(int key);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint MapVirtualKeyEx(uint code, uint type, IntPtr layout);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int ToUnicodeEx(uint key, uint scan, byte[] state, [Out] char[] chars,
            int length, uint flags, IntPtr layout);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out UIntPtr result);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, StringBuilder lParam,
            uint flags, uint timeout, out UIntPtr result);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);

        public static IntPtr Listen(int type, Hook procedure)
        {
            IntPtr hook = SetWindowsHookEx(type, procedure, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            return hook;
        }

        public static Field ActiveField()
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero) return new Field();
            uint thread = GetWindowThreadProcessId(window, IntPtr.Zero);
            if (thread == 0) return new Field();
            var info = new GuiInfo { Size = Marshal.SizeOf(typeof(GuiInfo)) };
            if (!GetGUIThreadInfo(thread, ref info) || (info.Flags & 0x1e) != 0 || info.Focus == IntPtr.Zero)
                return new Field();
            // Editors such as Notepad can host their focused control on a different UI thread.
            uint focusThread = GetWindowThreadProcessId(info.Focus, IntPtr.Zero);
            if (focusThread == 0) return new Field();
            return new Field { Window = window, Focus = info.Focus, Layout = GetKeyboardLayout(focusThread) };
        }

        public static bool Down(int key) { return GetAsyncKeyState(key) < 0; }
        public static bool ShortcutDown() { return Down(0x11) || Down(0x12) || Down(0x5b) || Down(0x5c); }
        public static int Stroke(uint scan)
        {
            return (int)scan * 4 + (Down(0x10) ? 1 : 0) + ((GetKeyState(0x14) & 1) != 0 ? 2 : 0);
        }

        public static bool? VerifyNativeSuffix(Field field, string suffix)
        {
            if (!field.Matches(ActiveField())) return false;
            if (!IsNativeEdit(field.Focus)) return null;
            int style = GetWindowLong(field.Focus, -16);
            if ((style & 0x820) != 0) return false; // Read-only or password edit.
            int start, end;
            if (!GetEditSelection(field.Focus, out start, out end) || end > 16000) return null;
            if (start != end || end < suffix.Length) return false;
            string text = GetEditText(field.Focus, end);
            return text == null ? (bool?)null : HasWordBoundary(text, suffix);
        }

        public static bool VerifyAccessibleSuffix(Field field, string suffix)
        {
            if (!field.Matches(ActiveField())) return false;
            try
            {
                var element = AutomationElement.FocusedElement;
                if (element == null || !Equals(element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true), false))
                    return false;
                var type = element.Current.ControlType;
                object pattern, value;
                if (!element.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) return false;
                bool hasValue = element.TryGetCurrentPattern(ValuePattern.Pattern, out value);
                if (hasValue && ((ValuePattern)value).Current.IsReadOnly) return false;
                var selection = ((TextPattern)pattern).GetSelection();
                if (selection == null || selection.Length != 1) return false;
                var caret = selection[0];
                if (type != ControlType.Edit && type != ControlType.Document && !hasValue
                    && !Equals(caret.GetAttributeValue(TextPattern.IsReadOnlyAttribute), false))
                    return false;
                if (caret.CompareEndpoints(TextPatternRangeEndpoint.Start, caret, TextPatternRangeEndpoint.End) != 0)
                    return false;
                var preceding = caret.Clone();
                if (preceding.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -suffix.Length) != -suffix.Length)
                    return false;
                string text = preceding.GetText(suffix.Length + 1);
                if (text.Length != suffix.Length || !EndsWithSuffix(text, suffix)) return false;
                return preceding.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -1) == 0
                    || HasWordBoundary(preceding.GetText(suffix.Length + 1), suffix);
            }
            catch (Exception)
            {
                // Cross-process accessibility providers can go away or fail during a query.
                return false;
            }
        }

        private static bool IsNativeEdit(IntPtr window)
        {
            var name = new StringBuilder(128);
            GetClassName(window, name, name.Capacity);
            string windowClass = name.ToString();
            return windowClass.Equals("Edit", StringComparison.OrdinalIgnoreCase)
                || windowClass.StartsWith("WindowsForms10.EDIT.", StringComparison.OrdinalIgnoreCase)
                || windowClass.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)
                || windowClass.StartsWith("WindowsForms10.RichEdit", StringComparison.OrdinalIgnoreCase);
        }

        private static bool GetEditSelection(IntPtr window, out int start, out int end)
        {
            start = end = 0;
            UIntPtr result;
            if (SendMessageTimeout(window, EditGetSelection, IntPtr.Zero, IntPtr.Zero, 2, 40, out result) == IntPtr.Zero)
                return false;
            uint selection = unchecked((uint)result.ToUInt64());
            if (selection == uint.MaxValue) return false;
            start = (int)(selection & 0xffff);
            end = (int)(selection >> 16);
            return start <= end;
        }

        private static string GetEditText(IntPtr window, int length)
        {
            UIntPtr result;
            var buffer = new StringBuilder(length + 1);
            return SendMessageTimeout(window, WindowGetText, new IntPtr(buffer.Capacity), buffer, 2, 40, out result) != IntPtr.Zero
                && result.ToUInt64() == (ulong)length && buffer.Length == length ? buffer.ToString() : null;
        }

        private static bool HasWordBoundary(string text, string suffix)
        {
            if (!EndsWithSuffix(text, suffix)) return false;
            int before = text.Length - suffix.Length - 1;
            return before < 0 || (!char.IsLetterOrDigit(text[before]) && text[before] != '_' && text[before] != '\'');
        }

        private static bool EndsWithSuffix(string text, string suffix)
        {
            if (text.EndsWith(suffix, StringComparison.Ordinal)) return true;
            // Rich editors may represent the final typed Space as a non-breaking space.
            return text.Length >= suffix.Length && text[text.Length - 1] == '\u00a0' && suffix[suffix.Length - 1] == ' '
                && string.CompareOrdinal(text, text.Length - suffix.Length, suffix, 0, suffix.Length - 1) == 0;
        }

        public static bool Replace(string oldText, string newText, Field field, IntPtr layout)
        {
            if (!field.Matches(ActiveField()) || ShortcutDown() || Down(0x10)) return false;
            var inputs = new Input[(oldText.Length + newText.Length) * 2];
            int i = 0;
            foreach (char unused in oldText)
            {
                inputs[i++] = MakeInput(Backspace, 0, 0);
                inputs[i++] = MakeInput(Backspace, 0, KeyUpFlag);
            }
            foreach (char letter in newText)
            {
                inputs[i++] = MakeInput(0, letter, UnicodeFlag);
                inputs[i++] = MakeInput(0, letter, UnicodeFlag | KeyUpFlag);
            }
            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input))) != inputs.Length) return false;
            if (layout != field.Layout)
                PostMessage(field.Focus, 0x50, IntPtr.Zero, layout);
            return true;
        }

        private static Input MakeInput(ushort key, ushort scan, uint flags)
        {
            return new Input { Type = 1, Data = new InputData
            {
                Keyboard = new KeyboardInput { Key = key, Scan = scan, Flags = flags, Extra = OwnInput }
            }};
        }
    }
}
