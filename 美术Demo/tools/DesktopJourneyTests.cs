using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace NetBunny
{
    // Real controller snapshots; hidden actors, private saves, no desktop input
    // and no system maintenance. These are short functional checks, not a soak.
    internal static class DesktopJourneyTests
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        static int checks, failures;
        static void Check(bool okay, string text) { checks++; Console.WriteLine((okay ? "PASS " : "FAIL ") + text); if (!okay) failures++; }
        static object Value(object o, string n) { FieldInfo f = o.GetType().GetField(n, F); return f == null ? null : f.GetValue(o); }
        static string Text(object o, string n) { return Value(o, n) as string ?? ""; }
        static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
        static AdventureEnemy Enemy(string id) { return new AdventureEnemy { Id = id, IsVirtual = true, MonsterKind = "imp", Health = 90, MaxHealth = 90 }; }
        [STAThread] static int Main(string[] args)
        {
            try
            {
                using (Form owner = new Form())
                using (AdventureController c = new AdventureController(owner, Path.GetFullPath(args[0]), false, null, false, Path.Combine(Path.GetFullPath(args[1]), "journey-" + Guid.NewGuid().ToString("N"))))
                {
                    ((Timer)Value(c, "timer")).Stop(); c.ConfigureDesktopDemo(false); ((Timer)Value(c, "timer")).Stop();
                    Set(c, "area", new Rectangle(-22000, -22000, 1500, 1000)); Set(c, "x", -21700.0); Set(c, "y", -21700.0);
                    c.Battle.Enemies.Clear();
                    AdventureEnemy ordinary = Enemy("ordinary"), warning = Enemy("warning"), mirror = Enemy("mirror");
                    warning.IsVirtual = false; warning.IsBoss = true; warning.IsDiskBoss = true;
                    mirror.NoFieldClear = true; mirror.IsBoss = true;
                    c.Battle.Enemies.Add(ordinary); c.Battle.Enemies.Add(warning); c.Battle.Enemies.Add(mirror);
                    DesktopPlayerView view = c.ReadDesktopPlayerView();
                    Check(view.Enemies.Any(e => e.Id == ordinary.Id), "ordinary enemy remains selectable in the same controller");
                    Check(!(bool)c.DesktopChooseEnemy(warning.Id, false), "disk warning is rejected by production selection");
                    DesktopEnemyChoice w = view.Enemies.First(e => e.Id == warning.Id);
                    Check(Value(w, "CanSelect") is bool && !(bool)Value(w, "CanSelect"), "disk warning is visibly nonselectable rather than a dead button");
                    Check(w.Action.Contains("空间") && !w.Action.Contains("已授权"), "disk warning explains manual space management and never promises deletion");
                    DesktopEnemyChoice m = view.Enemies.First(e => e.Id == mirror.Id);
                    Check(m.Source.Contains("蜃楼") && !m.Source.Contains("内存"), "mirror boss provenance identifies its expedition rather than computer memory");
                    Check(Text(view, "NextStep").Contains("逐招") && Text(view, "NextActionId") == "", "new mortal gets a per-strike instruction without a fake automatic victory action");
                    c.Cultivation.MortalKills = 10; view = c.ReadDesktopPlayerView();
                    Check(Text(view, "NextActionId") == "awaken" && Text(view, "NextActionText").Contains("觉醒"), "ten actual kill state offers the formal awakening action");
                    c.Battle.Progress.Dead = true; c.Battle.DeathTime = 1;
                    view = c.ReadDesktopPlayerView();
                    Check(Text(view, "NextActionId") == "" && Text(view, "NextStep").Contains("调息"), "exhaustion takes priority over awakening guidance");
                    c.Battle.Revive(); Check(c.AwakenSpiritRoot(), "formal awakening remains callable from the same identity");
                    c.Cultivation.Layer = 10; c.Cultivation.Qi = 0; view = c.ReadDesktopPlayerView();
                    Check(Text(view, "NextActionId") == "lingyu" && Text(view, "NextStep").Contains("蜃楼"), "eligible qi cultivator can discover the existing expedition");
                    c.Cultivation.Layer = c.Cultivation.RealmDef.LayersMax; c.Cultivation.BreakthroughInsight = 0;
                    view = c.ReadDesktopPlayerView();
                    Check(Text(view, "NextActionId") == "breakthrough" && Text(view, "NextStep").Contains("契机"), "full realm explains opportunity instead of suggesting a nonexistent next layer");
                    c.Cultivation.RealmIndex = 1; c.Cultivation.Layer = 1; c.Cultivation.Qi = 0;
                    Array.Clear(c.Dongtian.Fields, 0, c.Dongtian.Fields.Length); c.Dongtian.Bag.Clear(); view = c.ReadDesktopPlayerView();
                    Check(Text(view, "NextActionId") == "dongtian" && Text(view, "NextActionText").Contains("播种"), "first foundation state leads to actual cave planting");
                    c.Dongtian.Fields[0] = "linggu," + DateTime.UtcNow.AddHours(2).Ticks;
                    view = c.ReadDesktopPlayerView();
                    Check(Text(view, "NextStep").Contains("成熟") && !Text(view, "NextActionText").Contains("收获"), "immature crop is explained without claiming harvest readiness");
                    c.Dongtian.Fields[0] = "linggu," + DateTime.UtcNow.AddSeconds(-1).Ticks;
                    view = c.ReadDesktopPlayerView();
                    Check(Text(view, "NextActionText").Contains("收获"), "only real UTC-ready crop produces harvest navigation");
                    Check(!c.MaintenanceConsentCleanup && !c.MaintenanceConsentMemory && !c.MaintenanceConsentAutoMaintenance,
                        "guidance and source inspection grant no real maintenance authority");

                    var candidate = new CleanupCandidate();
                    candidate.Path = Path.Combine(Path.GetFullPath(args[1]), "own-fixture", "old-note.tmp");
                    candidate.Length = 731;
                    AdventureEnemy file = Enemy("file"); file.IsVirtual = false; file.Batch = new CleanupBatch(new List<CleanupCandidate> { candidate }, 7);
                    c.Battle.Enemies.Clear(); c.Battle.Enemies.Add(file); c.Battle.CancelQueue();
                    view = c.ReadDesktopPlayerView(); DesktopEnemyChoice f = view.Enemies.Single();
                    Check(Text(f, "SourceDetail").Contains(candidate.Path) && Text(f, "SourceDetail").Contains(file.Batch.Label), "file monster exposes the bound snapshot path and batch label without rescanning");
                    Check(f.Action.Contains("虚拟") || f.Action.Contains("关闭"), "virtual capability ceiling is reflected even for an unexpected file-backed monster");
                    Check(Value(f, "CanSelect") is bool && (bool)Value(f, "CanSelect"), "an ordinary unstarted monster stays selectable");
                    c.Battle.Authorize(file); view = c.ReadDesktopPlayerView();
                    Check(Text(view, "TargetSourceDetail").Contains(candidate.Path), "selected enemy source detail is available to the ecology panel");
                    string original = Text(view, "TargetSourceDetail"); candidate.Path = "changed fixture";
                    Check(Text(view, "TargetSourceDetail") == original, "returned source detail is detached from later batch mutations");
                    Check(Text(view, "TargetAction") == f.Action, "selected target and enemy list use the same truthful action explanation");
                }
                using (Form owner = new Form())
                using (AdventureController c = new AdventureController(owner, Path.GetFullPath(args[0]), false, null, false, Path.Combine(Path.GetFullPath(args[1]), "observe-" + Guid.NewGuid().ToString("N")), true))
                {
                    ((Timer)Value(c, "timer")).Stop();
                    AdventureEnemy file = Enemy("permissions"); file.IsVirtual = false;
                    file.Batch = new CleanupBatch(Enumerable.Range(0, 8).Select(i => new CleanupCandidate { Path = "C:\\OwnTestOnly\\file-" + i + ".tmp", Length = 16 }).ToList(), 2);
                    c.Battle.Enemies.Clear(); c.Battle.Enemies.Add(file);
                    Check(c.SetCleanupConsent(true), "file permission fixture persists only to its dedicated private archive");
                    c.Battle.SetAutomatic(true); file.Authorization = AdventureAuthorization.Automatic;
                    DesktopEnemyChoice before = c.ReadDesktopPlayerView().Enemies.Single();
                    Check(before.Action.Contains("自动维护未授权"), "file permission alone does not make an automatic battle promise maintenance");
                    Check(Text(before, "SourceDetail").Contains("file-4.tmp") && !Text(before, "SourceDetail").Contains("file-5.tmp") && Text(before, "SourceDetail").Contains("另有 3"),
                        "source preview is bounded while disclosing the omitted count");
                    Check(c.SetAutoMaintenanceConsent(true), "automatic maintenance fixture is explicitly enabled in its private archive");
                    Check(c.ReadDesktopPlayerView().Enemies.Single().Action.Contains("尝试清理"), "both valid grants describe an attempt rather than asserting completed cleanup");
                    Check(c.SetAutoMaintenanceConsent(false) && c.SetCleanupConsent(false), "fixture revokes both private grants after inspecting the view");
                    c.Battle.Authorize(file);
                    using (DesktopEcologyPanel panel = new DesktopEcologyPanel(c))
                    {
                        TextBox source = (TextBox)Value(panel, "targetSource");
                        int changes = 0; source.TextChanged += delegate { changes++; };
                        file.Health--; panel.UpdateState();
                        Check(changes == 0, "combat HP changes do not reset source detail text, selection or scroll position");
                        Check(source.GetLineFromCharIndex(source.TextLength) >= 8, "native Windows multiline control renders each source path and instruction on separate lines");
                    }
                    AdventureEnemy process = Enemy("chosen-program"); process.IsVirtual = false;
                    process.ProcessTarget = new ProcessIdentity { Id = 123456, Name = "owned-fixture", ExecutablePath = "C:\\OwnTestOnly\\fixture.exe", StartedUtcTicks = 1024 };
                    c.Battle.Enemies.Clear(); c.Battle.Enemies.Add(process);
                    DesktopEnemyChoice p = c.ReadDesktopPlayerView().Enemies.Single();
                    Check(Text(p, "SourceDetail").Contains("123456") && Text(p, "SourceDetail").Contains("fixture.exe") && Text(p, "SourceDetail").Contains("1024"),
                        "program monster details expose all three recorded identity components");
                    Check(p.Action.Contains("单独确认") && p.Action.Contains("不会自动关闭"), "program action never inherits automatic file or memory authority");
                    Check(!c.DesktopProcessBusy && c.ReadDesktopPlayerView().MaintenanceResults.Length == 0, "source and grant display inspection never dispatches any real maintenance worker");
                }
                Console.WriteLine("Desktop journey: " + checks + " checks, " + failures + " failures.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 2; }
        }
    }
}
