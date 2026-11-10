using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

[assembly: AssemblyTitle("SpaceFix")]
[assembly: AssemblyProduct("SpaceFix")]
[assembly: AssemblyDescription("Помощник для английской, русской и казахской раскладок")]
[assembly: AssemblyVersion("0.6.6.0")]
[assembly: AssemblyFileVersion("0.6.6.0")]

namespace LayoutPilot
{
    static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool background = args.Contains("--background", StringComparer.OrdinalIgnoreCase);
            bool first;
            // Share the lock with earlier releases so only one keyboard hook is active.
            using (var instance = new Mutex(false, "Local\\LayoutPilot.48D02E1B", out first))
            {
                if (!first)
                {
                    if (!background)
                        MessageBox.Show("SpaceFix уже работает. Его значок находится рядом с часами.", "SpaceFix");
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    using (var app = new Pilot(!background)) Application.Run(app);
                }
                catch (Win32Exception error)
                {
                    MessageBox.Show("Не удалось запустить SpaceFix.\n" + error.Message,
                        "SpaceFix", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }

    sealed class Pilot : ApplicationContext
    {
        private sealed class Word
        {
            public string Original;
            public Field Field;
            public int At;
        }

        private sealed class Undo
        {
            public string Original, Replacement;
            public Field Field;
            public int At;
        }

        private readonly PersonalDictionary personal;
        private readonly Recognition recognition;
        private readonly Control dispatch = new Control();
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu = DemoWindow.CreateTrayMenu();
        private readonly Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        private readonly ToolStripMenuItem pause;
        private readonly Timer timer = new Timer { Interval = 125 };
        private readonly Native.Hook keyboardCallback, mouseCallback;
        private readonly List<int> strokes = new List<int>();
        private readonly Queue<int[]> recentWords = new Queue<int[]>();
        private readonly HashSet<IntPtr> accessibilityChecks = new HashSet<IntPtr>();
        private Field wordField;
        private bool skipWord, paused, closing;
        private IntPtr keyboardHook, mouseHook;
        private int lastKeyboardActivity = Environment.TickCount, lastHookInstall = Environment.TickCount;
        private int swallowedKey;
        private Word pending;
        private Undo undo;
        private Form demo, dictionaryWindow;

        public Pilot(bool showWindow)
        {
            personal = new PersonalDictionary(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SpaceFix", "personal-dictionary.tsv"));
            recognition = new Recognition(personal);
            dispatch.CreateControl();
            keyboardCallback = KeyboardHook;
            mouseCallback = MouseHook;
            menu.Items.Add("Панель управления", null, (sender, e) => ShowDemo());
            pause = new ToolStripMenuItem("Приостановить", null, (sender, e) => TogglePause());
            menu.Items.Add(pause);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Личный словарь", null, (sender, e) => ShowPersonalDictionary());
            menu.Items.Add("Словари и лицензии", null, (sender, e) => ShowCredits());
            menu.Items.Add("Выйти", null, (sender, e) => ExitThread());
            tray = new NotifyIcon { Icon = icon, Text = "SpaceFix • EN / RU / KK", ContextMenuStrip = menu };
            tray.DoubleClick += (sender, e) => ShowDemo();
            timer.Tick += (sender, e) => CheckExpiration();
            try
            {
                KeyboardMap.Installed();
                keyboardHook = Native.Listen(13, keyboardCallback);
                mouseHook = Native.Listen(14, mouseCallback);
                tray.Visible = true;
                timer.Start();
                if (showWindow) ShowDemo();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private static int Age(int tick) { return unchecked(Environment.TickCount - tick); }

        private IntPtr KeyboardHook(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                lastKeyboardActivity = Environment.TickCount;
                var key = (Native.KeyEvent)Marshal.PtrToStructure(data, typeof(Native.KeyEvent));
#if TESTING
                if (key.Extra != new UIntPtr(0x5453544c) && key.Extra != Native.OwnInput)
                    return Native.CallNextHookEx(keyboardHook, code, message, data);
#endif
                if (key.Extra != Native.OwnInput)
                {
                    int kind = message.ToInt32();
                    int vk = (int)key.Key;
                    if (vk == swallowedKey && swallowedKey != 0)
                    {
                        if (kind == Native.KeyUp || kind == Native.SystemKeyUp) swallowedKey = 0;
                        return new IntPtr(1);
                    }
                    if (!paused && (kind == Native.KeyDown || kind == Native.SystemKeyDown))
                    {
                        if (IsInjected(key)) ResetTyping(true);
                        else if (OnKey(vk, key))
                        {
                            swallowedKey = vk;
                            return new IntPtr(1);
                        }
                    }
                }
            }
            return Native.CallNextHookEx(keyboardHook, code, message, data);
        }

        private static bool IsInjected(Native.KeyEvent key)
        {
#if TESTING
            if (key.Extra == new UIntPtr(0x5453544c)) return false;
#endif
            return (key.Flags & 0x10) != 0;
        }

        private bool OnKey(int vk, Native.KeyEvent key)
        {
            if (vk == 0x10 || vk == 0xa0 || vk == 0xa1 || vk == 0x14) return false;
            if (!Native.ShortcutDown() && !Native.Down(0x10))
            {
                if (vk == Native.F10 && undo != null && Age(undo.At) < 8000)
                {
                    var action = undo;
                    var field = Native.ActiveField();
                    if (action.Field.SameField(field))
                    {
                        undo = null;
                        var restore = new Word { Field = field, Original = action.Replacement, At = Environment.TickCount };
                        pending = restore;
                        dispatch.BeginInvoke((Action)(() => Revert(action, restore)));
                        return true;
                    }
                }
            }
            Dismiss();
            undo = null;
            if (vk == 0x11 || vk == 0x12 || vk == 0x5b || vk == 0x5c || (vk >= 0xa2 && vk <= 0xa5)
                || Native.ShortcutDown())
            {
                ResetTyping(false);
                return false;
            }
            Field fieldNow = Native.ActiveField();
            if (dictionaryWindow != null && dictionaryWindow.IsHandleCreated && fieldNow.Window == dictionaryWindow.Handle)
            {
                ResetTyping(false);
                return false;
            }
            if (!wordField.Matches(fieldNow))
            {
                strokes.Clear();
                recentWords.Clear();
                skipWord = false;
                wordField = fieldNow;
            }
            if (vk == Native.Space)
            {
                if (!skipWord && strokes.Count > 0 && !Native.Down(0x10))
                {
                    int[] keys = strokes.ToArray();
                    if (keys.Length > 1 || personal.HasSingleLetterRules) FinishWord(fieldNow, keys, recentWords.ToArray());
                    if (recentWords.Count == 4) recentWords.Dequeue();
                    recentWords.Enqueue(keys);
                }
                else recentWords.Clear();
                strokes.Clear();
                skipWord = false;
                return false;
            }
            if (vk == Native.Backspace && !Native.Down(0x10))
            {
                recentWords.Clear();
                if (strokes.Count > 0) strokes.RemoveAt(strokes.Count - 1);
                else skipWord = true;
                return false;
            }
            if (vk == 0x0d || vk == 0x09 || (vk >= 0x21 && vk <= 0x28) || vk == 0x2e)
            {
                strokes.Clear();
                recentWords.Clear();
                skipWord = false;
                return false;
            }
            if (skipWord) return false;
            KeyboardMap map = fieldNow.Focus == IntPtr.Zero ? null : KeyboardMap.From(fieldNow.Layout);
            int stroke = Native.Stroke(key.Scan);
            if (map == null || (key.Flags & 1) != 0 || map.At(stroke) == '\0' || strokes.Count >= 48)
            {
                strokes.Clear();
                recentWords.Clear();
                skipWord = true;
            }
            else strokes.Add(stroke);
            return false;
        }

        private void FinishWord(Field field, int[] keys, int[][] context)
        {
            var word = new Word { Field = field, At = Environment.TickCount };
            pending = word;
            dispatch.BeginInvoke((Action)(() => Prepare(word, keys, context)));
        }

        private void Prepare(Word word, int[] keys, int[][] context)
        {
            if (pending != word) return;
            KeyboardMap current = KeyboardMap.From(word.Field.Layout);
            word.Original = current == null ? null : Recognition.Convert(keys, keys.Length, current);
            if (word.Original == null) { Dismiss(); return; }
            var choice = recognition.Find(word.Original, keys, current, KeyboardMap.Installed());
            if (choice == null) { Dismiss(); return; }

            string before = "", replacement = "";
            if (choice.Map.Language != current.Language)
            {
                for (int i = context.Length - 1; i >= 0; i--)
                {
                    string earlier = Recognition.Convert(context[i], context[i].Length, current);
                    string converted = recognition.ConvertContext(earlier, context[i], current, choice.Map);
                    if (converted == null) break;
                    before = earlier + " " + before;
                    replacement = converted + " " + replacement;
                }
            }
            VerifyReplacement(word, choice, before, replacement);
        }

        private void VerifyReplacement(Word word, Candidate choice, string before, string replacement)
        {
            CheckSuffix(word.Field, before + word.Original + " ", ok =>
            {
                if (pending != word) return;
                if (Age(word.At) > 400 || !word.Field.Matches(Native.ActiveField()))
                {
                    Dismiss();
                    return;
                }
                if (!ok && before.Length > 0)
                    VerifyReplacement(word, choice, "", "");
                else if (ok)
                    Apply(word, before + word.Original, replacement + choice.Text, choice.Map);
                else
                    Dismiss();
            });
        }

        private void Apply(Word word, string original, string replacement, KeyboardMap target)
        {
            Dismiss();
            if (Native.Replace(original + " ", replacement + " ", word.Field, target.Handle))
            {
                recentWords.Clear();
                undo = new Undo { Original = original, Replacement = replacement, Field = word.Field, At = Environment.TickCount };
            }
        }

        private void Revert(Undo action, Word restore)
        {
            // A new key or mouse click cancels even a verification already in flight.
            if (pending != restore) return;
            CheckSuffix(restore.Field, action.Replacement + " ", ok =>
            {
                if (pending != restore) return;
                Dismiss();
                if (ok && Age(restore.At) < 400)
                    Native.Replace(action.Replacement + " ", action.Original + " ", restore.Field, action.Field.Layout);
            });
        }

        private void CheckSuffix(Field field, string suffix, Action<bool> then)
        {
            ThreadPool.QueueUserWorkItem(state =>
            {
                bool? native = Native.VerifyNativeSuffix(field, suffix);
                bool ok;
                if (!native.HasValue)
                {
                    ok = VerifyAccessibility(field, suffix);
                }
                else
                {
                    if (!native.Value)
                    {
                        // Give the target a chance to process the physical Space.
                        Thread.Sleep(12);
                        native = Native.VerifyNativeSuffix(field, suffix);
                    }
                    ok = native == true;
                }
                try { dispatch.BeginInvoke((Action)(() => { if (!closing) then(ok); })); }
                catch (InvalidOperationException) { }
            });
        }

        private bool VerifyAccessibility(Field field, string suffix)
        {
            // An external accessibility provider may block. Allow one check per window.
            lock (accessibilityChecks)
            {
                if (accessibilityChecks.Count >= 4 || !accessibilityChecks.Add(field.Window)) return false;
            }
            try
            {
                if (Native.VerifyAccessibleSuffix(field, suffix)) return true;
                Thread.Sleep(12);
                return Native.VerifyAccessibleSuffix(field, suffix);
            }
            finally
            {
                lock (accessibilityChecks) accessibilityChecks.Remove(field.Window);
            }
        }

        private IntPtr MouseHook(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && !paused)
            {
                int kind = message.ToInt32();
                if (kind == 0x201 || kind == 0x204 || kind == 0x207 || kind == 0x20b || kind == 0x20a || kind == 0x20e)
                {
                    ResetTyping(false);
                }
            }
            return Native.CallNextHookEx(mouseHook, code, message, data);
        }

        private void CheckExpiration()
        {
            if (!paused && Age(lastKeyboardActivity) >= 3000 && Age(lastHookInstall) >= 3000)
                RenewKeyboardHook();
            if (pending != null && (Age(pending.At) > 500
                || !pending.Field.Matches(Native.ActiveField()))) Dismiss();
            if (undo != null && (Age(undo.At) > 8000 || !undo.Field.SameField(Native.ActiveField()))) undo = null;
        }

        private void RenewKeyboardHook()
        {
            // Windows can drop a slow low-level hook without notifying its owner.
            lastHookInstall = Environment.TickCount;
            IntPtr replacement;
            try { replacement = Native.Listen(13, keyboardCallback); }
            catch (Win32Exception) { return; }
            var previous = keyboardHook;
            keyboardHook = replacement;
            if (previous == replacement || !Native.UnhookWindowsHookEx(previous))
            {
                swallowedKey = 0;
                ResetTyping(false);
            }
        }

        private void Dismiss()
        {
            pending = null;
        }

        private void ResetTyping(bool waitForSpace)
        {
            strokes.Clear();
            recentWords.Clear();
            skipWord = waitForSpace;
            undo = null;
            Dismiss();
        }

        private void TogglePause()
        {
            paused = !paused;
            if (!paused) RenewKeyboardHook();
            pause.Text = paused ? "Возобновить" : "Приостановить";
            tray.Text = paused ? "SpaceFix • на паузе" : "SpaceFix • EN / RU / KK";
            if (demo != null && !demo.IsDisposed) DemoWindow.SetPaused(demo, paused);
            ResetTyping(false);
        }

        private void ShowDemo()
        {
            if (demo == null || demo.IsDisposed)
            {
                demo = DemoWindow.Create(icon, KeyboardMap.Installed(), TogglePause, ShowCredits, ShowPersonalDictionary);
                DemoWindow.SetPaused(demo, paused);
            }
            demo.Show();
            if (demo.WindowState == FormWindowState.Minimized) demo.WindowState = FormWindowState.Normal;
            demo.Activate();
        }

        private void ShowCredits() { DemoWindow.ShowCredits(icon); }

        private void ShowPersonalDictionary()
        {
            if (dictionaryWindow == null || dictionaryWindow.IsDisposed)
                dictionaryWindow = DemoWindow.CreatePersonalDictionary(icon, personal);
            dictionaryWindow.Show();
            if (dictionaryWindow.WindowState == FormWindowState.Minimized) dictionaryWindow.WindowState = FormWindowState.Normal;
            dictionaryWindow.Activate();
        }

        protected override void ExitThreadCore()
        {
            if (demo != null) demo.Close();
            if (dictionaryWindow != null) dictionaryWindow.Close();
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !closing)
            {
                closing = true;
                if (keyboardHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyboardHook);
                if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
                timer.Dispose();
                Dismiss();
                if (demo != null) demo.Dispose();
                if (dictionaryWindow != null) dictionaryWindow.Dispose();
                tray.Dispose();
                menu.Dispose();
                dispatch.Dispose();
                icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
