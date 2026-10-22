using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace NetBunny
{
    // Stopped production controller and hidden native controls. Ready timestamps
    // belong only to this private fixture, not a natural waiting-time claim.
    internal static class DesktopReturnTasksTests
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        static int checks, failures;
        static void Check(bool okay, string text) { checks++; if (!okay) failures++; Console.WriteLine((okay ? "PASS " : "FAIL ") + text); }
        static T Field<T>(object obj, string name) { return (T)obj.GetType().GetField(name, F).GetValue(obj); }
        static void Set(object obj, string name, object value) { obj.GetType().GetField(name, F).SetValue(obj, value); }
        static string Text(object obj, string name) { return obj == null ? "" : (string)obj.GetType().GetField(name, F).GetValue(obj); }
        static object[] Tasks(DesktopPlayerView view)
        {
            FieldInfo field = typeof(DesktopPlayerView).GetField("ReturnTasks", F);
            return field == null || field.GetValue(view) == null ? new object[0] : ((IEnumerable)field.GetValue(view)).Cast<object>().ToArray();
        }
        static object TaskOf(DesktopPlayerView view, string route) { return Tasks(view).FirstOrDefault(t => Text(t, "PanelId") == route); }
        static Control[] Children(Control root) { return root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Children(c))).ToArray(); }
        static bool OwnVisible(Control c) { return c != null && (bool)typeof(Control).GetMethod("GetState", F).Invoke(c, new object[] { 2 }); }
        static Button TaskButton(Control panel, string route) { return Children(panel).OfType<Button>().FirstOrDefault(c => c.Name == "return-task-" + route); }
        static void Layout(Control root) { IntPtr h = root.Handle; foreach (Control child in root.Controls) Layout(child); root.PerformLayout(); }
        static string Files(string root)
        {
            using (SHA256 sha = SHA256.Create())
                return String.Join("|", Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p)
                    .Select(p => p + ":" + BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p)))).ToArray());
        }
        static string State(AdventureController g)
        {
            var l = g.Lingyu; var d = g.Dongtian; var b = g.Battle;
            Func<Dictionary<string, int>, string> bag = v => String.Join("|", v.OrderBy(p => p.Key).Select(p => p.Key + ":" + p.Value));
            return l.ExpeditionActive + "/" + l.ExpeditionNode + "/" + l.CombatPending + "/" + l.CombatOutcome + "/" + l.PendingJiyuanId
                + "/" + l.EventCounter + "/" + l.Grants.Count + "/" + l.PendingQi.Count + "/" + l.ExpeditionLog.Count + "/" + bag(l.Materials)
                + "/" + d.Level + "/" + String.Join("|", d.Fields) + "/" + d.Furnace + "/" + d.Forge + "/" + bag(d.Bag)
                + "/" + d.Grants.Count + "/" + d.Buffs.Count + "/" + d.Gears.Count + "/" + b.QueueCount + "/" + b.Enemies.Count
                + "/" + b.Progress.Health + "/" + b.Progress.Experience + "/" + b.Progress.FreedBytes + "/" + g.Cultivation.Qi;
        }
        static void Populate(AdventureController g)
        {
            g.Cultivation.Mortal = false; g.Cultivation.RealmIndex = 1; g.Cultivation.Layer = g.Cultivation.LayersMax;
            g.Battle.Progress.Dead = false; g.Lingyu.RealmEntered.Add("qianmian"); g.Lingyu.ExpeditionNode = "echo";
            g.Lingyu.ExpeditionActive = false; g.Lingyu.CombatPending = false; g.Lingyu.PendingJiyuanId = "jing-wen-1";
            g.Lingyu.JiyuanChoices.Remove("jing-wen-1"); g.Lingyu.Grants.Remove("jiyuan-jing-wen-1");
            long ready = DateTime.UtcNow.AddMinutes(-1).Ticks;
            g.Dongtian.Level = 1; Array.Clear(g.Dongtian.Fields, 0, g.Dongtian.Fields.Length);
            g.Dongtian.Fields[0] = "linggu," + ready; g.Dongtian.Fields[1] = "ningshen," + ready;
            g.Dongtian.Fields[2] = "jianxin," + ready; // Locked field must not enter the advertised count.
            g.Dongtian.Furnace = "juqi," + ready; g.Dongtian.Forge = "putuan," + ready;
        }
        static void ViewChecks(AdventureController g, string profile)
        {
            Populate(g); Check(g.DesktopSaveNow(), "five real archives are established only in the private fixture profile");
            string state = State(g), files = Files(profile); DesktopPlayerView view = g.ReadDesktopPlayerView();
            Check(view.NextActionId == "breakthrough", "the existing full-realm breakthrough next-step remains unchanged");
            Check(Tasks(view).Length == 2 && TaskOf(view, "lingyu") != null && TaskOf(view, "dongtian") != null,
                "full realm, suspended expedition and ready cave produce two independent return tasks together");
            string journey = Text(TaskOf(view, "lingyu"), "Text"), cave = Text(TaskOf(view, "dongtian"), "Text");
            Check(journey.Contains("暂存") && journey.Contains("待抉择") && journey.Contains(JiyuanEngine.Find("jing-wen-1").Name),
                "suspended formal pending offer is named as a saved choice, not a fresh event");
            Check(cave.Contains("灵田2") && cave.Contains("丹炉1") && cave.Contains("器位1") && !cave.Contains("灵田3"),
                "actual UTC-ready field, pill and gear counts are separate and ignore locked fields");
            g.Battle.Progress.Dead = true; view = g.ReadDesktopPlayerView();
            Check(Tasks(view).Length == 2 && view.NextActionId == "", "recovery guidance keeps both read-only return tasks visible while suppressing the original combat action");
            g.Battle.Progress.Dead = false; g.Lingyu.CombatPending = true; g.Lingyu.CombatOutcome = "final";
            view = g.ReadDesktopPlayerView();
            Check(Text(TaskOf(view, "lingyu"), "Text").Contains("待战") && !Text(TaskOf(view, "lingyu"), "Text").Contains("待抉择"),
                "pending real expedition combat takes precedence over a stale pending-offer ID without spawning anything");
            g.Lingyu.ExpeditionActive = true; view = g.ReadDesktopPlayerView();
            Check(TaskOf(view, "lingyu") != null && !Text(TaskOf(view, "lingyu"), "Text").Contains("暂存"), "an active unresolved battle is discoverable without falsely calling it suspended");
            g.Lingyu.CombatPending = false; g.Lingyu.CombatOutcome = ""; view = g.ReadDesktopPlayerView();
            Check(Text(TaskOf(view, "lingyu"), "Text").Contains("待抉择"), "an active valid pending choice remains a distinct return task");
            g.Lingyu.PendingJiyuanId = "unknown-offer"; view = g.ReadDesktopPlayerView();
            Check(TaskOf(view, "lingyu") == null, "an unknown saved offer cannot invent a pending choice for an otherwise active journey");
            g.Lingyu.ExpeditionActive = false; view = g.ReadDesktopPlayerView();
            Check(Text(TaskOf(view, "lingyu"), "Text").Contains("暂存") && !Text(TaskOf(view, "lingyu"), "Text").Contains("待抉择"), "ordinary suspended node remains resumable without fabricating a choice");
            g.Lingyu.ExpeditionNode = ""; g.Lingyu.RealmEntered.Clear(); g.Lingyu.PendingJiyuanId = "";
            Array.Clear(g.Dongtian.Fields, 0, g.Dongtian.Fields.Length); long future = DateTime.UtcNow.AddHours(2).Ticks;
            g.Dongtian.Fields[0] = "linggu," + future; g.Dongtian.Furnace = "juqi," + future; g.Dongtian.Forge = "putuan," + future;
            Check(Tasks(g.ReadDesktopPlayerView()).Length == 0, "empty/immature old-compatible states have no return tasks and no invented harvest");
            Populate(g); state = State(g); view = g.ReadDesktopPlayerView(); object first = Tasks(view).FirstOrDefault();
            if (first != null) Set(first, "Text", "fixture mutation");
            Check(first != null && !Text(TaskOf(g.ReadDesktopPlayerView(), "lingyu"), "Text").Contains("fixture mutation"), "return tasks are detached snapshots, not a second mutable persistent state");
            for (int i = 0; i < 20; i++) g.ReadDesktopPlayerView();
            Check(State(g) == state && Files(profile) == files, "reading return tasks does not enter/resume, spawn, collect, consume, reward or save");
        }
        static void PanelChecks(AdventureController g, string profile, string output)
        {
            object session = FormatterServices.GetUninitializedObject(typeof(DesktopDemoSession));
            Set(session, "<Game>k__BackingField", g); Set(session, "<ProfileRoot>k__BackingField", profile);
            using (DesktopPracticePanel panel = new DesktopPracticePanel((DesktopDemoSession)session))
            {
                Field<Timer>(panel, "refresh").Stop(); Populate(g); panel.UpdateState();
                Button journey = TaskButton(panel, "lingyu"), cave = TaskButton(panel, "dongtian");
                Check(journey != null && cave != null && OwnVisible(journey) && OwnVisible(cave), "two real native return buttons exist independently of the current growth button");
                FieldInfo navigate = typeof(DesktopPracticePanel).GetField("returnTaskNavigate", F);
                Action<string> actual = navigate == null ? null : (Action<string>)navigate.GetValue(panel);
                Check(actual != null && actual.Target == session && actual.Method.Name == "OpenPanel", "production return buttons use the existing session.OpenPanel navigation only");
                var routes = new List<string>(); string state = State(g), files = Files(profile);
                if (navigate != null) navigate.SetValue(panel, new Action<string>(routes.Add));
                if (journey != null && cave != null)
                    foreach (Button button in new[] { journey, cave }) typeof(Button).GetMethod("OnClick", F).Invoke(button, new object[] { EventArgs.Empty });
                Check(routes.SequenceEqual(new[] { "lingyu", "dongtian" }) && State(g) == state && Files(profile) == files,
                    "actual Click handlers forward exactly the two existing panel IDs and perform no game or archive mutation");
                int controls = Children(panel).Length;
                for (int i = 0; i < 20; i++) panel.UpdateState();
                Check(journey != null && cave != null && Object.ReferenceEquals(journey, TaskButton(panel, "lingyu"))
                    && Object.ReferenceEquals(cave, TaskButton(panel, "dongtian")) && controls == Children(panel).Length,
                    "repeated live refresh reuses the same two controls without allocating/replacing rows");
                foreach (Size size in new[] { new Size(390, 490), new Size(470, 700) })
                {
                    panel.ClientSize = size;
                    for (int i = 0; i < 3; i++) { Layout(panel); typeof(DesktopPracticePanel).GetMethod("ResizeColumns", F).Invoke(panel, null); }
                    Layout(panel);
                    Button[] buttons = new[] { journey, cave }.Where(b => b != null).ToArray();
                    Check(buttons.Length == 2 && buttons.All(b => b.Width > 0 && b.Right + b.Margin.Right <= b.Parent.ClientSize.Width + 2), size + ": both tasks stay inside the narrow native content column");
                    Check(buttons.Length == 2 && buttons.All(b => TextRenderer.MeasureText(b.Text, b.Font, new Size(Math.Max(1, b.Width - 16), Int32.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + 10 <= b.Height),
                        size + ": full real task text wraps without clipping its native button");
                    Label[] texts = Children(panel).OfType<Label>().Where(l => l.Name.StartsWith("return-task-text-", StringComparison.Ordinal)).ToArray();
                    Check(texts.Length == 2 && texts.All(l => l.Text == Text(TaskOf(g.ReadDesktopPlayerView(), l.Name.Substring("return-task-text-".Length)), "Text")
                        && l.GetPreferredSize(new Size(l.Width, 0)).Height <= l.Height + 2 && l.Right + l.Margin.Right <= l.Parent.ClientSize.Width - 92 + 2)
                        && buttons.All(b => b.Text == Text(TaskOf(g.ReadDesktopPlayerView(), (string)b.Tag), "ActionText")),
                        size + ": full summaries use actual wrapping native labels; short navigation labels remain fully visible");
                    using (Bitmap bitmap = new Bitmap(panel.Width, panel.Height)) { panel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, "return-tasks-" + size.Width + "-hidden-native-controls.png")); }
                }
                g.Lingyu.ExpeditionNode = ""; g.Lingyu.PendingJiyuanId = ""; g.Lingyu.RealmEntered.Clear();
                Array.Clear(g.Dongtian.Fields, 0, g.Dongtian.Fields.Length); g.Dongtian.Furnace = g.Dongtian.Forge = null; panel.UpdateState();
                Check(journey != null && cave != null && !OwnVisible(journey) && !OwnVisible(cave), "no pending state hides both task controls instead of showing empty placeholders");
                Populate(g); panel.UpdateState();
                Check(journey != null && cave != null && OwnVisible(journey) && OwnVisible(cave) && Object.ReferenceEquals(journey, TaskButton(panel, "lingyu")) && Object.ReferenceEquals(cave, TaskButton(panel, "dongtian")), "reappearing tasks restore the original control identities");
                Check(!panel.Visible && !g.DesktopHeroWindow.Visible && !g.DesktopActorsVisible && !Field<Timer>(panel, "refresh").Enabled && !Field<Timer>(g, "timer").Enabled,
                    "all fixture windows stay hidden and both simulation and panel timers stay stopped");
                Check(g.DesktopRealWorkIdle && !g.MaintenanceConsentCleanup && !g.MaintenanceConsentMemory && !g.MaintenanceConsentAutoMaintenance,
                    "return guidance grants no maintenance authority and starts no system worker");
            }
            GC.KeepAlive(session); // Deliberately unconstructed navigation shell is never disposed.
        }
        [STAThread] static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                string output = Path.GetFullPath(args[1]), profile = Path.Combine(output, "private-profile"); Directory.CreateDirectory(output);
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[2]));
                using (Form owner = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-25000, -25000) })
                using (AdventureController g = new AdventureController(owner, Path.GetFullPath(args[0]), false, null, false, profile))
                {
                    Field<Timer>(g, "timer").Stop(); g.ConfigureDesktopDemo(false); Field<Timer>(g, "timer").Stop();
                    ViewChecks(g, profile); PanelChecks(g, profile, output);
                }
                Console.WriteLine("Desktop return tasks: " + checks + " checks, " + failures + " failures. Private ready-state fixture, hidden native controls; no GUI input, natural waiting-time claim or system maintenance.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 2; }
        }
    }
}
