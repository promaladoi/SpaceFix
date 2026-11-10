using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

static class Integration
{
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Key, Scan;
        public uint Flags, Time;
        public UIntPtr Extra;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputData Data;
    }

    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] events, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyEx(uint code, uint type, IntPtr layout);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint key, uint scan, byte[] state, [Out] char[] buffer, int length, uint flags, IntPtr layout);

    private static int failures;
    private static Form window;
    private static TextBox edit, password;
    private static RichTextBox rich;
    private static System.Windows.Controls.TextBox wpf;
    private static InputLanguage en, ru, kk;

    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        var languages = InputLanguage.InstalledInputLanguages.Cast<InputLanguage>().ToList();
        en = languages.Single(language => language.Culture.LCID == 0x0409);
        ru = languages.Single(language => language.Culture.LCID == 0x0419);
        kk = languages.Single(language => language.Culture.LCID == 0x043f);
        window = new Form { Width = 500, Height = 250, Text = "LayoutPilot — integration fixture", TopMost = true };
        edit = new TextBox { Left = 12, Top = 20, Width = 420, Multiline = true, Height = 32 };
        password = new TextBox { Left = 12, Top = 65, Width = 420, UseSystemPasswordChar = true };
        rich = new RichTextBox { Left = 12, Top = 102, Width = 420, Height = 62 };
        wpf = new System.Windows.Controls.TextBox();
        var hosted = new ElementHost { Left = 12, Top = 175, Width = 420, Height = 30, Child = wpf };
        window.Controls.Add(edit);
        window.Controls.Add(password);
        window.Controls.Add(rich);
        window.Controls.Add(hosted);
        window.Shown += async (sender, e) =>
        {
            try { await Run(); }
            catch (Exception error) { Console.WriteLine(error); failures++; }
            finally { window.Close(); }
        };
        Application.Run(window);
        return failures == 0 ? 0 : 1;
    }

    private static async Task Run()
    {
        for (int attempt = 0; GetForegroundWindow() != window.Handle && attempt < 100; attempt++)
            await Task.Delay(300);

        await Reset(en, edit);
        await Type("привет", ru);
        Check("EN -> RU", edit.Text == "привет " && GetKeyboardLayout(0) == ru.Handle, edit.Text);
        Tap(0x79, 0);
        await Task.Delay(230);
        Check("F10 undo restores text and layout", edit.Text == "ghbdtn " && GetKeyboardLayout(0) == en.Handle, edit.Text);

        await Reset(ru, edit);
        await Type("hello", en);
        Check("RU -> EN", edit.Text == "hello " && GetKeyboardLayout(0) == en.Handle, edit.Text);

        await Reset(en, edit);
        await Type("сәлем", kk);
        Check("EN -> KK, including the Kazakh number row", edit.Text == "сәлем " && GetKeyboardLayout(0) == kk.Handle, edit.Text);

        await Reset(kk, edit);
        await Type("hello", en);
        Check("KK -> EN", edit.Text == "hello " && GetKeyboardLayout(0) == en.Handle, edit.Text);

        await Reset(en, edit);
        await Type("май", ru);
        Check("Shared Russian and Kazakh spelling is replaced automatically",
            edit.Text == "май " && GetKeyboardLayout(0) == ru.Handle, edit.Text);

        await Reset(en, edit);
        await Type("все", ru);
        Check("dct is replaced automatically", edit.Text == "все " && GetKeyboardLayout(0) == ru.Handle, edit.Text);

        await Reset(en, edit);
        await Type("прииет", ru);
        Check("Wrong-layout word with one typo is replaced automatically",
            edit.Text == "привет " && GetKeyboardLayout(0) == ru.Handle, edit.Text);

        await Reset(en, edit);
        await Type("helllo", en);
        Check("Repeated letter is corrected in the current layout",
            edit.Text == "hello " && GetKeyboardLayout(0) == en.Handle, edit.Text);

        await Reset(ru, edit);
        await Type("ошыбок", ru);
        Check("Russian spelling typo is corrected without switching",
            edit.Text == "ошибок " && GetKeyboardLayout(0) == ru.Handle, edit.Text);

        await Reset(en, edit);
        await Type("Hello", en);
        Check("Correct text stays intact", edit.Text == "Hello " && GetKeyboardLayout(0) == en.Handle, edit.Text);

        await Reset(en, edit);
        edit.Text = "old text";
        Send(0x11, 0, 0);
        Tap(0x41, 0);
        Send(0x11, 0, 2);
        await Task.Delay(60);
        await Type("привет", ru);
        Check("First word after Ctrl+A is corrected", edit.Text == "привет " && GetKeyboardLayout(0) == ru.Handle, edit.Text);

        await Reset(en, edit);
        await Type("Привет", ru);
        Check("Capitalization is preserved", edit.Text == "Привет " && GetKeyboardLayout(0) == ru.Handle, edit.Text);

        await Reset(en, password);
        await Type("привет", ru);
        Check("Password field is skipped", password.Text == "ghbdtn " && GetKeyboardLayout(0) == en.Handle, "protected");

        await Reset(en, rich);
        await Type("привет", ru);
        Check("Rich-text editor supports automatic replacement", rich.Text == "привет " && GetKeyboardLayout(0) == ru.Handle, rich.Text);

        await Reset(en, rich);
        KeyPressEventHandler nonBreakingSpace = (sender, e) =>
        {
            if (e.KeyChar != ' ') return;
            rich.SelectedText = "\u00a0";
            e.Handled = true;
        };
        rich.KeyPress += nonBreakingSpace;
        await Type("привет", ru);
        rich.KeyPress -= nonBreakingSpace;
        Check("Non-breaking Space is supported", rich.Text == "привет\u00a0" && GetKeyboardLayout(0) == ru.Handle, rich.Text);

        InputLanguage.CurrentInputLanguage = en;
        wpf.Focus();
        await Task.Delay(120);
        await Type("привет", ru);
        Check("Accessibility-backed editor supports automatic replacement", wpf.Text == "привет " && GetKeyboardLayout(0) == ru.Handle, wpf.Text);
    }

    private static async Task Reset(InputLanguage language, TextBoxBase field)
    {
        window.Activate();
        field.Focus();
        field.Clear();
        InputLanguage.CurrentInputLanguage = language;
        Tap(0x1b, 0);
        Tap(0x20, 0);
        await Task.Delay(125);
        field.Clear();
    }

    private static async Task Type(string word, InputLanguage keyPositions)
    {
        foreach (char letter in word)
        {
            var key = Find(letter, keyPositions.Handle);
            if (key.Value) Send(0x10, 0, 0);
            Tap(0, key.Key);
            if (key.Value) Send(0x10, 0, 2);
            await Task.Delay(22);
        }
        Tap(0x20, 0);
        await Task.Delay(250);
    }

    private static KeyValuePair<ushort, bool> Find(char expected, IntPtr layout)
    {
        var state = new byte[256];
        var output = new char[8];
        for (int shift = 0; shift <= 1; shift++)
        {
            state[0x10] = shift == 1 ? (byte)128 : (byte)0;
            for (ushort scan = 2; scan <= 0x35; scan++)
            {
                uint key = MapVirtualKeyEx(scan, 3, layout);
                if (ToUnicodeEx(key, scan, state, output, output.Length, 4, layout) == 1 && output[0] == expected)
                    return new KeyValuePair<ushort, bool>(scan, shift == 1);
            }
        }
        throw new InvalidOperationException("Missing key position for " + expected);
    }

    private static void Tap(ushort key, ushort scan)
    {
        Send(key, scan, 0);
        Send(key, scan, 2);
    }

    private static void Send(ushort key, ushort scan, uint flags)
    {
        if (GetForegroundWindow() != window.Handle) throw new InvalidOperationException("Fixture is not foreground");
        var input = new Input
        {
            Type = 1, Data = new InputData
            {
                Keyboard = new KeyboardInput { Key = key, Scan = scan,
                    Flags = flags | (scan == 0 ? 0u : 8u), Extra = new UIntPtr(0x5453544c) }
            }
        };
        if (SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input))) != 1)
            throw new InvalidOperationException("Unable to deliver a test keystroke");
    }

    private static void Check(string label, bool ok, string actual)
    {
        if (!ok) failures++;
        Console.WriteLine("{0} {1} [{2}]", ok ? "PASS" : "FAIL", label, actual);
    }
}
