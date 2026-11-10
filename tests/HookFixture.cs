using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
namespace LayoutPilot {
  static class HookFixture {
    [STAThread] static void Main(string[] args) {
      Application.EnableVisualStyles();
      using (var pilot = new Pilot(false))
      using (var wait = new Timer { Interval = 100 })
      {
        if (args.Length == 1)
        {
          var field = typeof(Pilot).GetField("keyboardHook", BindingFlags.NonPublic | BindingFlags.Instance);
          var original = (IntPtr)field.GetValue(pilot);
          if (!Native.UnhookWindowsHookEx(original))
            throw new InvalidOperationException("Unable to simulate the lost keyboard hook");
          wait.Tick += (sender, e) =>
          {
            if ((IntPtr)field.GetValue(pilot) == original) return;
            File.WriteAllText(args[0], "Keyboard hook restored after forced removal.");
            wait.Stop();
          };
          wait.Start();
        }
        Application.Run(pilot);
      }
    }
  }
}
