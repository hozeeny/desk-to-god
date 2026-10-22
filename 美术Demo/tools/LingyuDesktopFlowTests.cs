using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace NetBunny
{
    // Hidden/private functional fixtures, never a natural exploration or native
    // mouse test. Story/HP/queue setup is explicit and all archives are private.
    internal static class LingyuDesktopFlowTests
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static int checks, failures;
        static void Check(bool okay, string message) { checks++; if (!okay) failures++; Console.WriteLine((okay ? "PASS " : "FAIL ") + message); }
        static T Field<T>(object instance, string name) { FieldInfo field = instance.GetType().GetField(name, Hidden); return field == null ? default(T) : (T)field.GetValue(instance); }
        static void Set(object instance, string name, object value) { instance.GetType().GetField(name, Hidden).SetValue(instance, value); }
        static object Call(object instance, string method, params object[] args) { return instance.GetType().GetMethod(method, Hidden).Invoke(instance, args); }
        static void Click(Button button) { typeof(Button).GetMethod("OnClick", Hidden).Invoke(button, new object[] { EventArgs.Empty }); }
        static void Layout(Control root) { IntPtr handle = root.Handle; foreach (Control child in root.Controls) Layout(child); root.PerformLayout(); }
        static Rectangle InForm(Control control, Form form) { return new Rectangle(form.PointToClient(control.PointToScreen(Point.Empty)), control.Size); }
        static AdventureController Game(Form owner, string assets, string profile)
        {
            AdventureController game = new AdventureController(owner, assets, false, null, false, profile);
            Field<Timer>(game, "timer").Stop(); game.ConfigureDesktopDemo(false); Field<Timer>(game, "timer").Stop();
            game.Cultivation.Mortal = false; game.Cultivation.Layer = 12; game.Cultivation.AutoBattle = true; game.Battle.SetAutomatic(true);
            return game;
        }
        static void Offer(AdventureController game)
        {
            game.Lingyu.RealmEntered.Add("qianmian"); game.Lingyu.ExpeditionActive = true; game.Lingyu.ExpeditionNode = "echo";
            Set(game.Mirage, "pendingJiyuan", JiyuanEngine.All.First(j => j.Id == "jing-wen-1"));
            FieldInfo pending = typeof(LingyuProgress).GetField("PendingJiyuanId", Hidden);
            if (pending != null) pending.SetValue(game.Lingyu, "jing-wen-1");
        }
        static void RecoveryChecks(Form owner, string assets, string output)
        {
            using (AdventureController game = Game(owner, assets, Path.Combine(output, "recovery")))
            {
                Offer(game); game.Battle.Progress.Dead = true; game.Battle.DeathTime = 1;
                using (LingyuForm panel = new LingyuForm(game))
                {
                    Field<Timer>(panel, "refresh").Stop(); panel.RefreshData();
                    Check(Field<Button[]>(panel, "choices").All(b => !b.Enabled), "recovery keeps event choices readable but disables consuming actions");
                    Check(Field<Label>(panel, "expeditionHint").Text.Contains("调息"), "the expedition panel explains the actual recovery gate");
                    game.LingyuChoose(1);
                    Check(!game.Lingyu.JiyuanChoices.ContainsKey("jing-wen-1") && !game.Lingyu.CombatPending,
                        "a direct choice during recovery cannot spend its one-time event or start an unwinnable battle");
                    Check(!game.Battle.Enemies.Any(e => e.NoFieldClear), "a rejected recovery choice creates no mirror actor");
                    game.Battle.Revive(); panel.RefreshData();
                    Check(Field<Button[]>(panel, "choices")[1].Enabled, "the same event choice becomes available after real recovery");
                    game.LingyuChoose(1);
                    Check(game.Lingyu.CombatPending && game.Lingyu.JiyuanChoices["jing-wen-1"] == 1,
                        "after recovery the original one-time choice starts its normal combat");
                }
            }
            using (AdventureController game = Game(owner, assets, Path.Combine(output, "dead-entry")))
            {
                game.Battle.Progress.Dead = true;
                Check(!game.LingyuEnter("qianmian") && !game.Lingyu.ExpeditionActive, "direct realm entry is rejected during recovery without advancing exploration");
            }
        }
        static void SuspendChecks(Form owner, string assets, string output)
        {
            string profile = Path.Combine(output, "suspend-restart");
            using (AdventureController game = Game(owner, assets, profile))
            {
                Offer(game); game.LingyuChoose(1);
                AdventureEnemy mirror = game.Battle.Enemies.Single(e => e.NoFieldClear);
                game.DesktopChooseEnemy(mirror.Id, false);
                AdventureEnemy realJob = new AdventureEnemy { Id = "unrelated-owned-job-fixture", Health = 50, MaxHealth = 50 };
                game.Battle.Enemies.Add(realJob); game.Battle.Authorize(realJob); realJob.Cleaning = realJob.CleanupStarted = true;
                Check(realJob.Authorized && game.Battle.QueueCount == 2, "the unrelated started-order fixture is genuinely in the same battle queue");
                long score = game.Battle.Progress.Score, qi = game.Cultivation.LifetimeQi;
                using (LingyuForm panel = new LingyuForm(game))
                {
                    Field<Timer>(panel, "refresh").Stop(); panel.RefreshData();
                    Button pause = Field<Button>(panel, "suspendExpedition");
                    Check(pause != null && pause.Enabled, "an independent suspend action exists even while mirror combat owns all choices");
                    panel.ClientSize = new Size(390, 490); for (int i = 0; i < 3; i++) Layout(panel);
                    Check(pause != null && panel.ClientRectangle.Contains(InForm(pause, panel)), "suspend remains in the first viewport of a narrow panel");
                    if (pause != null) Click(pause); else game.LingyuSuspend();
                    Check(!game.Lingyu.ExpeditionActive && game.Lingyu.CombatPending, "suspend preserves the pending event battle instead of recording a loss");
                    Check(!game.Battle.Enemies.Contains(mirror) && game.Battle.Target != mirror, "suspend withdraws the exact mirror from actors and combat focus");
                    Check(realJob.Authorized && realJob.CleanupStarted && game.Battle.Enemies.Contains(realJob) && game.Battle.QueueCount == 1 && realJob.QueueNumber == 1,
                        "suspend never cancels unrelated queued or started maintenance");
                    Check(game.AutoPlanning && game.Cultivation.AutoBattle, "leaving the expedition preserves automatic desktop practice preference");
                    Check(game.Battle.Progress.Score == score && game.Cultivation.LifetimeQi == qi && game.Lingyu.Materials.Count == 0,
                        "suspend awards no kill, experience, qi or event material");
                    game.LingyuSuspend();
                    Check(!game.Lingyu.ExpeditionActive && game.Lingyu.CombatPending, "a duplicate suspend remains idempotent");
                    Check(game.DesktopSaveNow(), "the paused pending expedition saves through all five private production archives");
                    LingyuProgress saved = LingyuProgress.Load(Path.Combine(profile, "lingyu-v1.txt"));
                    Check(!saved.ExpeditionActive && saved.CombatPending && saved.CombatOutcome == "jiyuan:jing-wen-1", "production Load retains the paused one-time combat semantics");
                }
            }
            using (AdventureController game = Game(owner, assets, profile))
            {
                Check(!game.TryResumeLingyuCombat() && !game.Battle.Enemies.Any(e => e.NoFieldClear), "controller reconstruction does not silently restart an explicitly paused battle");
                Set(game, "desktopLastRefill", -100.0); Call(game, "MaintainDesktopField");
                Check(game.Battle.Enemies.Any(e => e.IsVirtual && !e.NoFieldClear), "paused pending combat does not suppress ordinary desktop encounters");
                using (LingyuForm panel = new LingyuForm(game))
                {
                    Field<Timer>(panel, "refresh").Stop(); panel.RefreshData();
                    Button resume = Field<Button>(panel, "continueExpedition");
                    Check(resume != null && resume.Enabled, "reconstructed paused state offers an explicit continue action");
                    if (resume != null) Click(resume); else game.LingyuEnter("qianmian");
                    AdventureEnemy mirror = game.Battle.Enemies.Single(e => e.NoFieldClear);
                    Check(game.Lingyu.ExpeditionActive && game.Lingyu.CombatPending && mirror.IsVirtual, "continue restores exactly one virtual mirror for the saved event");
                    Check(!game.TryResumeLingyuCombat() && game.Battle.Enemies.Count(e => e.NoFieldClear) == 1, "repeated repair checks cannot duplicate the restored mirror");
                    game.Battle.Authorize(mirror); mirror.Health = 1; game.Battle.Cooldown = 0; game.Battle.Strike(mirror);
                    game.LingyuSuspend();
                    Check(game.Lingyu.ExpeditionActive && game.Battle.Enemies.Contains(mirror), "an already defeated mirror is settled before any suspend can discard its reward");
                    Call(game, "BeginCleanup", mirror);
                    Check(!game.Lingyu.CombatPending && game.Lingyu.Materials.ContainsKey("suijing-jing") && game.Lingyu.Materials["suijing-jing"] == 1,
                        "the resumed actual strike settles its original event reward once");
                    Call(game, "BeginCleanup", mirror);
                    Check(game.Lingyu.Materials.ContainsKey("suijing-jing") && game.Lingyu.Materials["suijing-jing"] == 1, "a duplicate old combat callback cannot duplicate material");
                    Check(!panel.Visible && !owner.Visible && !game.DesktopHeroWindow.Visible && game.DesktopRealWorkIdle,
                        "all exploration fixtures remain hidden and start no real maintenance");
                }
            }
        }
        static void FailedSaveChecks(Form owner, string assets, string output)
        {
            using (AdventureController game = Game(owner, assets, Path.Combine(output, "failed-save")))
            {
                Offer(game); game.LingyuChoose(1);
                string blocked = Path.Combine(output, "unwritable-lingyu-destination"); Directory.CreateDirectory(blocked);
                Set(game, "lingyuPathOverride", blocked);
                try
                {
                    game.LingyuSuspend();
                    Check(!game.Lingyu.ExpeditionActive && game.Lingyu.CombatPending && game.LingyuPersistencePending,
                        "an unwritable private save leaves the expedition safely paused with an explicit pending state");
                    Check(game.ReadDesktopPlayerView().RecentEvents.Any(e => e.Contains("暂未保存")), "failed suspension persistence cannot claim a durable successful save");
                    using (LingyuForm panel = new LingyuForm(game))
                    {
                        Field<Timer>(panel, "refresh").Stop(); panel.RefreshData();
                        Check(Field<Label>(panel, "expeditionHint").Text.Contains("暂未保存"), "the actual panel exposes a pending save instead of a false saved message");
                    }
                }
                finally { Set(game, "lingyuPathOverride", null); }
                Check(game.DesktopSaveNow() && !game.LingyuPersistencePending, "restoring the private destination allows the original pending state to persist");
            }
        }
        [STAThread] static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                string assets = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[2]));
                using (Form owner = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-25000,-25000) })
                { RecoveryChecks(owner, assets, output); SuspendChecks(owner, assets, output); FailedSaveChecks(owner, assets, output); }
                Console.WriteLine("Lingyu desktop flow: " + checks + " checks, " + failures + " failures; hidden/private story and queue fixtures only.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
    }
}
