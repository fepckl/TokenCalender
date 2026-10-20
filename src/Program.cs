using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ClaudeUsageTracker
{
    static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int pid);

        [STAThread]
        static int Main(string[] args)
        {
            // `ClaudeUsageTracker.exe --once` prints the current usage and exits (handy for scripts).
            if (args.Length > 0 && args[0] == "--once") return PrintOnce();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // `--calendar` opens only the token calendar, independent of the tray instance.
            if (args.Length > 0 && args[0] == "--calendar")
            {
                Application.Run(new CalendarForm());
                return 0;
            }

            bool created;
            using (new Mutex(true, @"Local\ClaudeUsageTracker.SingleInstance", out created))
            {
                if (!created) return 0;
                Application.Run(new TrayApp());
            }
            return 0;
        }

        static int PrintOnce()
        {
            AttachConsole(-1);
            try
            {
                var snap = new UsageService(AppSettings.Load()).Fetch();
                Console.WriteLine("Quelle: {0}  Plan: {1}", snap.Source, snap.Plan ?? "-");
                Print(snap.Session);
                Print(snap.Weekly);
                foreach (var m in snap.Models) Print(m);
                if (snap.ExtraLimit.HasValue)
                    Console.WriteLine("Extra: {0:0.00} / {1:0.00} {2}", snap.ExtraUsed ?? 0, snap.ExtraLimit.Value, snap.ExtraCurrency);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        static void Print(UsageWindow w)
        {
            Console.WriteLine("{0,-24} {1,6}  {2}  [{3}]", w.Name, Format.Percent(w.Percent), Format.Countdown(w.ResetsAtUtc), w.Status());
        }
    }
}
