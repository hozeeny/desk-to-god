using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace NetBunny
{
    // Actual controllers, private archives, stopped timers and hidden actors.
    // These checks catch the idle-aggression, queued-selection and stale-feedback
    // regressions without sending input to the user's desktop.
    internal static class DesktopPlayerViewTests
    {
        static int checks, failures;
        static BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, flags).GetValue(instance); }
        static void Set(object instance, string name, object value) { instance.GetType().GetField(name, flags).SetValue(instance, value); }
        static object Call(object instance, string name, params object[] args) { return instance.GetType().GetMethod(name, flags).Invoke(instance, args); }
        static void Check(bool okay, string message) { checks++; Console.WriteLine((okay ? "PASS " : "FAIL ") + message); if (!okay) failures++; }
        static string Text(object view, string name) { return Field<string>(view, name); }
        static AdventureEnemy Enemy(string id, AdventureController c)
        {
            AdventureEnemy e = new AdventureEnemy { Id = id, IsVirtual = true, MonsterKind = "imp", MaxHealth = 144, Health = 144, SpawnLevel = 1 };
            Size size = (Size)Call(c, "EnemySize", e);
            e.X = Field<double>(c, "x") + 110 + 80 - size.Width / 2.0;
            e.Y = Field<double>(c, "y") + 240 - size.Height;
            e.TargetX = e.X + 300; e.TargetY = e.Y;
            c.Battle.Enemies.Add(e); return e;
        }
        [STAThread] static int Main(string[] args)
        {
            try
            {
                string assets = Path.GetFullPath(args[0]), root = Path.GetFullPath(args[1]);
                Directory.CreateDirectory(root);
                using (Form owner = new Form())
                using (AdventureController c = new AdventureController(owner, assets, false, null, false, Path.Combine(root, "player-" + Guid.NewGuid().ToString("N"))))
                {
                    Field<Timer>(c, "timer").Stop(); c.ConfigureDesktopDemo(false); Field<Timer>(c, "timer").Stop();
                    Set(c, "area", new Rectangle(-22000, -22000, 1500, 1000)); Set(c, "x", -21700.0); Set(c, "y", -21700.0);
                    c.DesktopAdvanceForTest(.01); // Original Tick initializes the realm damage/defense factors.
                    c.Battle.Enemies.Clear(); AdventureEnemy a = Enemy("a", c), b = Enemy("b", c);
                    int hp = c.Battle.Progress.Health;
                    for (int i = 0; i < 400; i++) Call(c, "AttackHero", a, true, .1);
                    Check(c.Battle.Progress.Health == hp && !c.Battle.Progress.Dead, "unselected mortal opponent cannot damage a player who has not begun combat");
                    c.Battle.Revive(); double beforeX = a.X;
                    Call(c, "AdvanceEnemyPatrol", a, true, .1);
                    Check(a.X > beforeX, "unselected ordinary mortal opponent follows its patrol destination instead of chasing the hero");
                    a.X = beforeX; // The next attack fixture starts at the original 80-pixel range.
                    c.Battle.Authorize(a); hp = c.Battle.Progress.Health;
                    Call(c, "AttackHero", a, true, 3.0);
                    Check(c.Battle.Progress.Health == hp - 6, "selected mortal opponent retains its original damage and attack cooldown");
                    MethodInfo choose = typeof(AdventureController).GetMethod("DesktopChooseEnemy", flags);
                    MethodInfo read = typeof(AdventureController).GetMethod("ReadDesktopPlayerView", flags);
                    Check(choose != null, "desktop selection API exists for both actor clicks and the accessible enemy list");
                    Check(read != null, "desktop player view exposes current objectives, action readiness and event history");
                    if (choose != null && read != null)
                    {
                        c.Battle.Cooldown = 0; int originalHealth = b.Health;
                        Check((bool)choose.Invoke(c, new object[] { "b", true }) && c.Battle.Target == b && !a.Authorized,
                            "clicking B immediately replaces manual target A without retaining a hidden attack queue");
                        Check(b.Health == originalHealth - 18 && c.Battle.Cooldown > .5, "one enemy click makes at most one unmodified mortal strike");
                        choose.Invoke(c, new object[] { "b", true });
                        Check(b.Health == originalHealth - 18, "a repeated click during weapon cooldown never adds damage");
                        Check(!(bool)choose.Invoke(c, new object[] { "missing", true }) && c.Battle.Target == b, "invalid enemy ID cannot clear the current selection");
                        object view = read.Invoke(c, null);
                        Check(Text(view, "Objective").Contains("0/10") && Text(view, "TargetName").Length > 0
                            && !Field<bool>(view, "StrikeReady") && Text(view, "AttackText").Contains("冷却"), "view explains mortal progress, selected enemy and attack cooldown");
                        c.Battle.Cooldown = 0; b.X += 600; view = read.Invoke(c, null);
                        Check(!Field<bool>(view, "StrikeReady") && Text(view, "AttackText").Contains("接近"), "view explains approach range instead of claiming an available strike");
                        c.Battle.Progress.Dead = true; c.Battle.Progress.Health = 0; c.Battle.DeathTime = 10.2; view = read.Invoke(c, null);
                        Check(Field<int>(view, "RestSeconds") == 50 && Text(view, "Notice").Contains("调息") && !Text(view, "Notice").Contains("墓碑"),
                            "mortal recovery view derives its real remaining time and suppresses legacy tombstone instructions");
                        c.Battle.DeathTime = 34.2; view = read.Invoke(c, null);
                        Check(Field<int>(view, "RestSeconds") == 26, "recovery countdown continues decreasing with actual battle death time");
                        int panelRequests = 0; c.DesktopPanelRequested = delegate { panelRequests++; };
                        typeof(Control).GetMethod("OnMouseClick", flags).Invoke(c.DesktopHeroWindow, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 60, 60, 0) });
                        Check(panelRequests == 1, "clicking a resting hero opens the desktop practice panel instead of silently ignoring the player");
                        int beforeEquipmentRequest = panelRequests; c.OpenEquipment();
                        Check(panelRequests == beforeEquipmentRequest + 1, "legacy equipment requests remain redirected to the isolated desktop practice panel");
                        c.Battle.Revive(); c.Cultivation.MortalKills = 10; c.Battle.LastReward = "ordinary spawn notice"; view = read.Invoke(c, null);
                        Check(Text(view, "Objective").Contains("觉醒") && Text(view, "Notice").Contains("觉醒"), "ten-kill awakening objective survives unrelated enemy spawn messages");
                        Check(c.AwakenSpiritRoot(), "view integration leaves original ten-kill awakening callable");
                        view = read.Invoke(c, null);
                        Check(Text(view, "Notice").Contains(c.Cultivation.RootName) && Text(view, "Objective").Contains("120"),
                            "awakening identity is visible with the next real cultivation target");
                        c.Battle.CancelQueue(); c.Battle.SetAutomatic(false); c.Battle.Authorize(a);
                        Check((bool)choose.Invoke(c, new object[] { "b", false }) && c.Battle.Target == b && !a.Authorized && !c.AutoPlanning,
                            "after awakening manual target B immediately replaces A while manual mode stays off");
                        c.Battle.CancelQueue(); c.Battle.SetAutomatic(true); c.Battle.Authorize(a);
                        bool autoPreference = c.Cultivation.AutoBattle;
                        Check((bool)choose.Invoke(c, new object[] { "b", false }) && c.Battle.Target == b && !a.Authorized,
                            "after awakening explicit target B takes priority even when automatic mode retains a manually selected A");
                        Check(c.AutoPlanning && c.Cultivation.AutoBattle == autoPreference,
                            "selecting a different automatic-combat target preserves both runtime automation and the saved preference");
                        Check(Field<bool>(view, "BlockReady") && c.TryBlock() && !Field<bool>(read.Invoke(c, null), "BlockReady"),
                            "guard readiness changes immediately after a real guard action without exposing a false ready button");
                        c.Battle.Progress.Dead = true; c.Battle.DeathTime = 10.2; c.Cultivation.RealmIndex = 1; view = read.Invoke(c, null);
                        Check(Field<int>(view, "RestSeconds") == 20, "foundation recovery uses its actual thirty-second duration rather than a fixed mortal timer");
                        c.Battle.Revive(); c.Cultivation.Mortal = true; c.Cultivation.MortalKills = 0; c.Battle.SetAutomatic(false);
                        b.X = a.X; c.Battle.Cooldown = 0;
                        for (int i = 0; i < 14; i++) choose.Invoke(c, new object[] { i % 2 == 0 ? "a" : "b", false });
                        view = read.Invoke(c, null); string[] events = Field<string[]>(view, "RecentEvents");
                        Check(events.Length <= 6 && events.Length >= 1, "recent events remain bounded while target selections change repeatedly");
                        string saved = events[0]; events[0] = "caller mutation";
                        Check(Field<string[]>(read.Invoke(c, null), "RecentEvents")[0] == saved, "returned event snapshots cannot mutate the controller's history");
                        AdventureEnemy real = Enemy("real-unsafe", c); real.IsVirtual = false; real.Cleaning = true; real.CleanupStarted = true;
                        Check(!(bool)choose.Invoke(c, new object[] { real.Id, true }) && real.Cleaning && real.CleanupStarted,
                            "isolated target selection refuses real or in-flight file work without cancelling it");
                        c.Cultivation.Mortal = false; c.Battle.CancelQueue(); real.Cleaning = false; real.CleanupStarted = false;
                        c.Battle.Authorize(real); real.Cleaning = true; real.CleanupStarted = true; c.Battle.SetAutomatic(true);
                        Check(!(bool)choose.Invoke(c, new object[] { "a", false }) && real.Authorized && real.Cleaning && real.CleanupStarted && c.AutoPlanning,
                            "post-awakening target changes refuse an in-flight file-backed queue without cancelling its accounting");
                        Check(!c.MaintenanceConsentCleanup && !c.MaintenanceConsentMemory && !c.MaintenanceConsentAutoMaintenance,
                            "desktop interaction never grants real maintenance permission");
                        c.Cultivation.Mortal = false; c.Battle.CancelQueue(); c.Battle.Progress.Health = 100;
                        a.X = Field<double>(c, "x") + 110 + 80 - ((Size)Call(c, "EnemySize", a)).Width / 2.0;
                        a.AttackTime = 0; Set(c, "blockUntil", -1.0);
                        Call(c, "AttackHero", a, true, 3.0);
                        Check(c.Battle.Progress.Health == 94, "qi-refining unselected enemies retain their original aggression");
                    }
                }
                using (Form owner = new Form())
                using (AdventureController legacy = new AdventureController(owner, assets, true))
                {
                    Field<Timer>(legacy, "timer").Stop(); Set(legacy, "x", -21700.0); Set(legacy, "y", -21700.0);
                    AdventureEnemy original = Enemy("legacy-rule", legacy);
                    int hp = legacy.Battle.Progress.Health;
                    Call(legacy, "AttackHero", original, true, 3.0);
                    Check(legacy.Battle.Progress.Health == hp - 6, "the original non-isolated controller retains unselected-enemy combat behavior");
                }
                Console.WriteLine("Desktop player regression: " + checks + " checks, " + failures + " failures.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 2; }
        }
    }
}
