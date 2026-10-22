using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace NetBunny
{
    // Bounded logical fixtures: actual reward/transfer/production/controller code,
    // private archives, stopped timers, no visible windows or system maintenance.
    internal static class DongtianMirageLoopTests
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        const string Material = "jinghua-shuiyue", Recipe = "ninglu-jinghua";
        static int checks, failures;
        static void Check(bool okay, string message) { checks++; Console.WriteLine((okay ? "PASS " : "FAIL ") + message); if (!okay) failures++; }
        static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, Hidden).GetValue(target); }
        static Button Action(DongtianForm panel, string text) { return panel.ActionControlsSnapshot().OfType<Button>().FirstOrDefault(b => b.Text == text); }
        static void Click(Button button) { typeof(Button).GetMethod("OnClick", Hidden).Invoke(button, new object[] { EventArgs.Empty }); }
        static int Stock(LingyuProgress p, string material) { int n; return p.Materials.TryGetValue(material, out n) ? n : 0; }
        static void ClearLord(AdventureController controller)
        {
            LingyuProgress progress = controller.Lingyu;
            progress.ExpeditionActive = true; progress.ExpeditionNode = "atrium";
            progress.CombatPending = true; progress.CombatOutcome = "final";
            controller.Mirage.OnCombatWon(new JiyuanContext { RealmIndex = 1, Layer = 12, MirageVisits = progress.Visits("qianmian"), Baiye = controller.Baiye, Lingyu = progress });
        }
        static void CoreChecks(string root)
        {
            DongtianPillDef recipe = DongtianRecipes.Pill(Recipe);
            Check(recipe != null, "repeatable mirror material has a production recipe");
            if (recipe == null) return;
            DateTime utc = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);
            DongtianProgress cave = new DongtianProgress(); cave.Bag[Material] = 1;
            Check(!cave.StartPill(Recipe, utc) && cave.CountOf(Material) == 1 && cave.Furnace == null,
                "one mirror material cannot pay the two-material recipe or lose a partial payment");
            cave.Bag[Material] = 2;
            Check(cave.StartPill(Recipe, utc) && cave.CountOf(Material) == 0 && cave.Furnace != null,
                "two materials produce exactly one mirror pill batch");
            Check(!cave.StartPill(Recipe, utc) && cave.CountOf(Material) == 0,
                "a duplicate start cannot create a second batch in the occupied furnace");
            string archive = Path.Combine(root, "core", "dongtian-v1.txt"); cave.Save(archive); cave = DongtianProgress.Load(archive);
            Check(!cave.CollectPill(utc.AddMinutes(29)) && cave.CollectPill(utc.AddMinutes(30)),
                "the saved mirror recipe matures after thirty minutes and can be collected offline");
            Check(Math.Abs(cave.DamageFactor(utc.AddMinutes(31)) - 1.08) < .000001
                && cave.DamageFactor(utc.AddMinutes(30).AddHours(12)) == 1,
                "mirror alchemy supplies eight percent damage for twelve hours after collection");
            cave.Bag["shuiyue-lingsui"] = 1;
            Check(cave.StartPill("ninglu", utc.AddHours(1)) && cave.CountOf("shuiyue-lingsui") == 0
                && !cave.CollectPill(utc.AddMinutes(89)) && cave.CollectPill(utc.AddMinutes(90)),
                "the original lingsui recipe still consumes one rare material and brews for thirty minutes");
            Check(cave.Buffs.Count == 1 && Math.Abs(cave.DamageFactor(utc.AddMinutes(91)) - 1.08) < .000001,
                "rare and repeatable recipes share one non-stacking damage buff");
            long expiry = cave.Buffs["buff-ninglu"];
            cave.Bag[Material] = 2; cave.StartPill(Recipe, utc); cave.CollectPill(utc.AddMinutes(30));
            Check(cave.Buffs["buff-ninglu"] == expiry && cave.DamageFactor(utc.AddMinutes(91)) < 1.081,
                "collecting an earlier-ending alternative cannot shorten or multiply the existing buff");
            Check(DongtianRecipes.MaterialName(Material) == "镜花水月", "imported mirror material keeps its player-facing name");
        }
        static void RewardAndImportChecks(string assets, string root)
        {
            string profile = Path.Combine(root, "profile");
            using (Form owner = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-25000, -25000) })
            {
                using (AdventureController controller = new AdventureController(owner, assets, false, null, false, profile))
                {
                    Field<Timer>(controller, "timer").Stop();
                    controller.Cultivation.Mortal = false; controller.Cultivation.RealmIndex = 1; controller.Cultivation.Layer = 12;
                    ClearLord(controller);
                    Check(controller.Lingyu.Visits("qianmian") == 1 && Stock(controller.Lingyu, Material) == 2 && Stock(controller.Lingyu, "shuiyue-lingsui") == 1,
                        "the first formal lord reward supplies two mirror materials and one rare lingsui");
                    using (DongtianForm panel = new DongtianForm(controller))
                    {
                        Field<Timer>(panel, "refresh").Stop(); panel.RefreshData();
                        Button import = Action(panel, "镜花水月入库");
                        Check(import != null && import.Enabled && import.AccessibleDescription.Contains("镜花凝露丹") && import.AccessibleDescription.Contains("2"),
                            "the available import explains the two-material mirror pill purpose before a click");
                        Button pill = Action(panel, "炼镜花凝露丹");
                        Check(pill != null && !pill.Enabled, "the mirror recipe is discoverable and disabled until its real bag payment exists");
                        if (import != null)
                        {
                            Click(import); panel.RefreshData();
                            Check(Stock(controller.Lingyu, Material) == 1 && controller.Dongtian.CountOf(Material) == 1,
                                "the import button transfers one actual reward unit rather than copying it");
                            Check(Object.ReferenceEquals(import, Action(panel, "镜花水月入库")), "inventory changes reuse the existing import control while stock remains");
                            Click(import); panel.RefreshData();
                        }
                        else { controller.DongtianImport(Material); controller.DongtianImport(Material); }
                        Check(!controller.DongtianImport(Material) && controller.Dongtian.CountOf(Material) == 2,
                            "an exhausted reward inventory cannot be imported again");
                        Check(controller.Lingyu.Grants.Keys.Count(k => k.StartsWith("dtin-" + Material + "-", StringComparison.Ordinal)) == 2
                            && controller.Dongtian.Grants.Count(k => k.StartsWith("dtin-" + Material + "-", StringComparison.Ordinal)) == 2,
                            "both original ledgers record exactly the two successful reward transfers");
                        ClearLord(controller);
                        Check(controller.Lingyu.Visits("qianmian") == 2 && Stock(controller.Lingyu, Material) == 2 && Stock(controller.Lingyu, "shuiyue-lingsui") == 1,
                            "the second formal lord reward replenishes mirror material without duplicating rare lingsui");
                        panel.RefreshData(); import = Action(panel, "镜花水月入库");
                        using (FileStream locked = new FileStream(controller.DongtianWritePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        {
                            if (import != null) Click(import); else controller.DongtianImport(Material);
                            panel.RefreshData();
                            Check(controller.DongtianPersistPending && controller.Dongtian.CountOf(Material) == 3 && Stock(controller.Lingyu, Material) == 1,
                                "a cave archive failure keeps the exact accepted reward transfer pending");
                            Button pending = Action(panel, "镜花水月入库");
                            Check(pending != null && !pending.Enabled, "pending mirror import visibly disables further production operations");
                            if (pending != null) Click(pending);
                            Check(controller.Dongtian.CountOf(Material) == 3 && Stock(controller.Lingyu, Material) == 1,
                                "a queued click during pending persistence cannot transfer another unit");
                        }
                        controller.TryFlushDongtianPersistence(); panel.RefreshData();
                        Check(!controller.DongtianPersistPending && DongtianProgress.Load(controller.DongtianWritePath).CountOf(Material) == 3
                            && Stock(LingyuProgress.Load(controller.LingyuWritePath), Material) == 1,
                            "the original retry durably commits the second-clear transfer to both archives");
                        controller.DongtianImport(Material); panel.RefreshData();
                        pill = Action(panel, "炼镜花凝露丹");
                        Check(pill != null && pill.Enabled, "repeated lord rewards enable the real mirror recipe");
                        Control[] stable = panel.ActionControlsSnapshot(); for (int i = 0; i < 5; i++) panel.RefreshData();
                        Check(stable.SequenceEqual(panel.ActionControlsSnapshot()), "unchanged mirror inventory and recipe states preserve all action controls");
                        if (pill != null) Click(pill);
                        Check(controller.Dongtian.CountOf(Material) == 2 && controller.Dongtian.Furnace != null && controller.Dongtian.Furnace.StartsWith(Recipe + ",", StringComparison.Ordinal),
                            "the same controller turns second-clear stock into a persisted mirror batch");
                        if (controller.Dongtian.Furnace != null)
                        {
                            // Only this private fixture's production deadline is advanced; real reward, payment and collection paths are unchanged.
                            controller.Dongtian.Furnace = Recipe + ",1";
                            Check(controller.DongtianCollectPill() && controller.Dongtian.BuffActive("buff-ninglu", DateTime.UtcNow),
                                "formal collection consumes the ready batch and activates its existing damage buff");
                            Check(!controller.DongtianCollectPill(), "a duplicate collection cannot issue another effect");
                            controller.ConfigureDesktopDemo(false); Field<Timer>(controller, "timer").Stop(); controller.DesktopAdvanceForTest(.1);
                            AdventureBattle battle = Field<AdventureBattle>(controller, "battle");
                            Check(Math.Abs(battle.CultivationDamageFactor / CultivationCombatPower.DamageFactor(controller.Cultivation) - 1.08) < .000001,
                                "the collected mirror pill feeds the production battle damage factor");
                        }
                    }
                    Check(controller.DesktopSaveNow() && controller.DesktopRealWorkIdle
                        && !owner.Visible && !Field<KnightLayeredWindow>(controller, "heroWindow").Visible,
                        "the complete reward loop saves only private archives with actors hidden and no real workers");
                }
                using (AdventureController restored = new AdventureController(owner, assets, false, null, false, profile))
                {
                    Field<Timer>(restored, "timer").Stop();
                    Check(restored.Lingyu.Visits("qianmian") == 2 && restored.Dongtian.CountOf(Material) == 2 && Stock(restored.Lingyu, Material) == 0,
                        "controller reconstruction reconciles the two clears without copying spent or imported materials");
                    Check(restored.Dongtian.BuffActive("buff-ninglu", DateTime.UtcNow) && !restored.DongtianImport(Material),
                        "the real persisted pill effect survives reconstruction without reopening exhausted imports");
                }
            }
        }
        [STAThread] static int Main(string[] args)
        {
            try
            {
                string root = Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
                Console.WriteLine("Private fixture root: " + root);
                CoreChecks(root); RewardAndImportChecks(Path.GetFullPath(args[0]), root);
                Console.WriteLine("Dongtian mirage loop: " + checks + " checks, " + failures + " failures. Logical/private fixtures only; no natural-growth or desktop acceptance claim.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
    }
}
