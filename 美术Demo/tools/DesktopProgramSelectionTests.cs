using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetBunny
{
    // Real hidden WinForms controls, synthetic candidates and a fresh private
    // controller. No process census, input, cleanup or program-close requests.
    internal static class DesktopProgramSelectionTests
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static readonly List<object> states = new List<object>();
        static int checks, failures;
        static void Check(bool okay, string name)
        { checks++; if (!okay) failures++; Console.WriteLine((okay ? "PASS " : "FAIL ") + name); }
        static T Get<T>(object target, string name)
        { return (T)target.GetType().GetField(name, Hidden).GetValue(target); }
        static void PumpUntil(Func<bool> ready)
        {
            Stopwatch wait = Stopwatch.StartNew();
            while (!ready())
            {
                if (clock.ElapsedMilliseconds > 20000 || wait.ElapsedMilliseconds > 3000)
                    throw new TimeoutException("Own hidden selection fixture exceeded its bounded wait.");
                Application.DoEvents(); Thread.Sleep(2);
            }
        }
        sealed class Observation : IDisposable
        {
            internal readonly ManualResetEventSlim Entered = new ManualResetEventSlim(), Release = new ManualResetEventSlim();
            internal ProcessIdentity[] Results;
            internal Exception Error;
            internal ProcessIdentity[] Read()
            {
                Entered.Set();
                if (!Release.Wait(3000)) throw new TimeoutException("Synthetic observation was not released.");
                if (Error != null) throw Error;
                return Results;
            }
            public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
        }
        sealed class Fixture : IDisposable
        {
            internal readonly Form Owner;
            internal readonly AdventureController Game;
            internal readonly DesktopEcologyPanel Panel;
            internal readonly ComboBox Programs;
            internal readonly Button Mark, Scan;
            internal readonly Label PathLabel;
            Observation current;
            internal int ObserverCalls;
            internal Fixture(string assets, string root, bool observe)
            {
                Owner = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                    Location = new Point(-24000, -24000), Size = new Size(1, 1) };
                IntPtr ownerHandle = Owner.Handle;
                Game = new AdventureController(Owner, assets, false, null, false, root, observe);
                // The constructor starts a UI timer. Stop it before any message
                // pumping: Tick can sample disks even while Enabled is false.
                Get<System.Windows.Forms.Timer>(Game, "timer").Stop();
                Check(!Game.Enabled && !Get<System.Windows.Forms.Timer>(Game, "timer").Enabled,
                    "fixture stops the default-off controller timer before any message pumping");
                Check(Idle(), "constructor has not started a watcher, census, disk/memory or maintenance worker");
                Panel = new DesktopEcologyPanel(Game, delegate { Interlocked.Increment(ref ObserverCalls); return current.Read(); });
                Panel.ShowInTaskbar = false; Panel.Location = new Point(-24000, -24000);
                Programs = Get<ComboBox>(Panel, "programs");
                Scan = Get<Button>(Panel, "scanPrograms"); PathLabel = Get<Label>(Panel, "programPath");
                var content = Get<FlowLayoutPanel>(Panel, "content");
                Mark = content.Controls.OfType<Button>().Single(b => b.Text == "将所选程序标记为程灵");
                IntPtr panelHandle = Panel.Handle, comboHandle = Programs.Handle, markHandle = Mark.Handle, scanHandle = Scan.Handle;
            }
            internal bool Idle()
            {
                return Get<object>(Game, "tempWatcher") == null && !Get<bool>(Game, "scanBusy")
                    && !Get<bool>(Game, "diskBusy") && !Get<bool>(Game, "memoryBusy")
                    && Get<object>(Game, "cleanupTask") == null && Get<object>(Game, "memoryReclaimTask") == null
                    && !Game.DesktopProcessBusy && Get<object>(Game, "connectivity") == null;
            }
            internal Task Begin(Observation observation)
            {
                current = observation;
                Task task = (Task)typeof(DesktopEcologyPanel).GetMethod("ScanPrograms", Hidden).Invoke(Panel, null);
                PumpUntil(() => observation.Entered.IsSet || task.IsCompleted);
                return task;
            }
            internal void Complete(Observation observation, Task task)
            {
                observation.Release.Set(); PumpUntil(() => task.IsCompleted);
                task.GetAwaiter().GetResult(); Panel.UpdateState();
            }
            internal void State(string name)
            {
                object state = new { name = name, seconds = clock.Elapsed.TotalSeconds,
                    panelHandle = Panel.Handle.ToInt64(), comboHandle = Programs.Handle.ToInt64(), markHandle = Mark.Handle.ToInt64(),
                    count = Programs.Items.Count, selected = Programs.SelectedIndex,
                    nativeSelected = SendMessage(Programs.Handle, 0x147, IntPtr.Zero, IntPtr.Zero).ToInt32(),
                    listEnabled = Programs.Enabled, nativeListEnabled = IsWindowEnabled(Programs.Handle),
                    markEnabled = Mark.Enabled, nativeMarkEnabled = IsWindowEnabled(Mark.Handle),
                    scanEnabled = Scan.Enabled, path = PathLabel.Text, visible = Panel.Visible };
                states.Add(state); Console.WriteLine("STATE " + new JavaScriptSerializer().Serialize(state));
            }
            internal bool Unselected()
            { return Programs.SelectedIndex == -1 && SendMessage(Programs.Handle, 0x147, IntPtr.Zero, IntPtr.Zero).ToInt32() == -1; }
            internal bool CannotMark()
            { return !Mark.Enabled && !IsWindowEnabled(Mark.Handle); }
            internal bool CannotChoose()
            { return !Programs.Enabled && !IsWindowEnabled(Programs.Handle); }
            public void Dispose()
            {
                Panel.Dispose(); Game.Dispose(); Owner.Dispose();
            }
        }
        static ProcessIdentity Candidate(int id, string name)
        { return new ProcessIdentity { Id = id, StartedUtcTicks = 638000000000000000L + id, ExecutablePath = @"C:\SyntheticSelectionFixture\" + name + ".exe", Name = name, WorkingSetBytes = id * 1024L }; }
        static void Exercise(string assets, string root)
        {
            ProcessIdentity first = Candidate(101, "SyntheticA"), second = Candidate(102, "SyntheticB");
            using (Fixture f = new Fixture(assets, Path.Combine(root, "observe-profile"), true))
            {
                Check(f.Game.DesktopObservesSystem && f.Game.DesktopMode == DesktopEnvironmentMode.Observe, "fixture uses the production private Observe policy without enabling gameplay");
                f.State("initial"); Check(f.Unselected() && f.CannotMark(), "no candidate can be marked before an explicit selection");
                using (Observation observation = new Observation { Results = new[] { first, second } })
                {
                    Task task = f.Begin(observation); f.Panel.UpdateState(); f.State("first-pending");
                    Check(f.CannotChoose() && f.CannotMark() && !f.Scan.Enabled, "pending observation disables the native list, mark and scan actions");
                    f.Complete(observation, task); f.State("first-success");
                    Check(f.Programs.Items.Count == 2 && f.Programs.Enabled, "successful observation exposes both synthetic candidates for manual choice");
                    Check(f.Unselected() && f.CannotMark(), "successful observation never preselects the first candidate");
                    Check(f.PathLabel.Text.Contains("选择"), "unselected successful observation visibly asks the player to choose");
                }
                f.Programs.SelectedIndex = 1; f.State("explicit-second");
                Check(f.Mark.Enabled && IsWindowEnabled(f.Mark.Handle) && f.PathLabel.Text == second.ExecutablePath,
                    "explicit selection enables marking and shows that exact program path");
                using (Observation observation = new Observation { Results = new[] { second, first } })
                {
                    Task task = f.Begin(observation); f.Panel.UpdateState(); f.State("refresh-pending");
                    Check(f.Unselected() && f.Programs.Items.Count == 0 && f.CannotChoose() && f.CannotMark(),
                        "refresh immediately invalidates the old selection and candidate snapshot");
                    f.Complete(observation, task); f.State("refresh-success");
                    Check(f.Programs.Items.Count == 2 && f.Unselected() && f.CannotMark(),
                        "reordered refresh still requires a fresh explicit choice");
                }
                f.Programs.SelectedIndex = 0;
                using (Observation observation = new Observation { Error = new InvalidOperationException("controlled observation failure") })
                {
                    Task task = f.Begin(observation); f.Complete(observation, task); f.State("refresh-failure");
                    Check(f.Unselected() && f.Programs.Items.Count == 0 && f.CannotChoose() && f.CannotMark(),
                        "failed observation cannot retain or mark an old candidate");
                    Check(f.PathLabel.Text.Contains("暂未完成观察") && f.Scan.Enabled, "failed observation explains failure and permits an explicit retry");
                }
                using (Observation observation = new Observation { Results = new ProcessIdentity[0] })
                {
                    Task task = f.Begin(observation); f.Complete(observation, task); f.State("empty-success");
                    Check(f.Unselected() && f.Programs.Items.Count == 0 && f.CannotChoose() && f.CannotMark(), "empty observation has no selectable or markable program");
                    Check(f.PathLabel.Text.Contains("当前没有"), "empty observation explains why no candidate is available");
                }
                using (Observation observation = new Observation { Results = new[] { first } })
                {
                    Task task = f.Begin(observation); f.Complete(observation, task);
                    f.Programs.SelectedIndex = 0; Check(f.Mark.Enabled && f.PathLabel.Text == first.ExecutablePath, "retry recovers and accepts a fresh explicit selection");
                    f.Programs.SelectedIndex = -1; f.State("selection-cleared");
                    Check(f.Unselected() && f.CannotMark(), "clearing an explicit selection immediately disables marking again");
                }
                Check(f.Idle() && !f.Owner.Visible && !f.Panel.Visible && !f.Game.DesktopHeroWindow.Visible
                    && !f.Game.MaintenanceConsentCleanup && !f.Game.MaintenanceConsentMemory && !f.Game.MaintenanceConsentAutoMaintenance,
                    "all controls remain hidden and every real observation/maintenance worker and scope remains inactive");
            }
            using (Fixture f = new Fixture(assets, Path.Combine(root, "virtual-profile"), false))
            using (Observation observation = new Observation { Results = new[] { first } })
            {
                observation.Release.Set();
                Check(!f.Scan.Enabled && f.CannotChoose() && f.CannotMark(), "Virtual policy disables observation, choosing and marking");
                Task task = f.Begin(observation); PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); f.Panel.UpdateState(); f.State("virtual-after-request");
                Check(f.ObserverCalls == 0 && f.Programs.Items.Count == 0 && f.CannotChoose() && f.CannotMark(),
                    "Virtual policy refuses the observation method before any injected worker is called");
                Check(f.Idle() && f.Game.DesktopMode == DesktopEnvironmentMode.Virtual, "Virtual request cannot start real workers or expand its policy");
            }
        }
        [STAThread] static int Main(string[] args)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            if (args.Length != 3) { Console.Error.WriteLine("Usage: DesktopProgramSelectionTests.exe assetsRoot evidenceRoot artRoot"); return 2; }
            string root = Path.GetFullPath(args[1]), error = null;
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[2]));
                Exercise(Path.GetFullPath(args[0]), root);
            }
            catch (Exception failure) { error = failure.ToString(); Check(false, "fixture exception: " + error); }
            var report = new { checks = checks, failures = failures, passed = failures == 0, error = error,
                seconds = clock.Elapsed.TotalSeconds, states = states,
                scope = "Actual hidden native production WinForms controls; fresh private Observe/Virtual profiles; controller timer stopped before pumping; controlled synthetic observation only. No real process census, cleanup, input, program-close request, user archive read or game-clock advance." };
            File.WriteAllText(Path.Combine(root, "program-selection.json"), new JavaScriptSerializer().Serialize(report), new System.Text.UTF8Encoding(true));
            Console.WriteLine("Program selection: " + checks + " checks, " + failures + " failures; " + clock.Elapsed.TotalSeconds.ToString("F2") + " seconds.");
            return failures == 0 ? 0 : 1;
        }
    }
}
