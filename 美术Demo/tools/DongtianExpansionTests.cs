using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace NetBunny
{
    // Real cave inventories, temporary archives and hidden, stopped-clock panels.
    // No application entry point, desktop actor rendering or live profile is used.
    internal static class DongtianExpansionTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int checks, failures;
        private static MethodInfo expand;
        private static void Check(bool okay, string message)
        {
            checks++; Console.WriteLine((okay ? "PASS " : "FAIL ") + message); if (!okay) failures++;
        }
        private static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, Hidden).GetValue(target); }
        private static bool Expand(DongtianProgress cave, int expected) { return (bool)expand.Invoke(cave, new object[] { expected }); }
        private static void Click(Button button) { typeof(Button).GetMethod("OnClick", Hidden).Invoke(button, new object[] { EventArgs.Empty }); }
        private static Button ExpandButton(DongtianForm form)
        {
            return form.ActionControlsSnapshot().OfType<Button>().FirstOrDefault(b => b.Text.StartsWith("扩建", StringComparison.Ordinal));
        }
        private static void Stock(DongtianProgress cave)
        {
            cave.Bag["linggu"] = 20; cave.Bag["ningshen"] = 10; cave.Bag["jianxin"] = 3;
        }
        private static void CoreChecks(string root)
        {
            expand = typeof(DongtianProgress).GetMethod("Expand", Hidden, null, new[] { typeof(int) }, null);
            Check(expand != null, "cave exposes an expected-level expansion operation");
            if (expand == null) return;
            DateTime start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DongtianProgress shortStock = new DongtianProgress();
            shortStock.Bag["linggu"] = 4; shortStock.Bag["ningshen"] = 1;
            Check(!Expand(shortStock, 1) && shortStock.Level == 1 && shortStock.CountOf("linggu") == 4 && shortStock.CountOf("ningshen") == 1,
                "an incomplete multi-material payment leaves every stock and level unchanged");
            Check(shortStock.LastNotice.Contains("不足"), "insufficient inventory produces a readable failure notice");

            DongtianProgress cave = new DongtianProgress(); Stock(cave);
            cave.Plant(0, "linggu", start); cave.Furnace = "juqi,123"; cave.Forge = "putuan,456";
            Check(cave.LastNotice.Contains("2 小时"), "planting notice agrees with the UI's actual-hour duration");
            cave.Buffs["buff-gushen"] = start.AddDays(2).Ticks; cave.Gears.Add("jianwan");
            cave.Grants.Add("dtin-shuiyue-lingsui-7"); cave.ImportSeq = 7; cave.Bag["shuiyue-lingsui"] = 2;
            string field = cave.Fields[0];
            Check(Expand(cave, 1) && cave.Level == 2 && cave.UnlockedFields == 3 && cave.CountOf("linggu") == 16 && cave.CountOf("ningshen") == 8 && cave.CountOf("jianxin") == 3,
                "a first expansion pays exactly one recipe and unlocks one extra field");
            Check(cave.Fields[0] == field && cave.Furnace == "juqi,123" && cave.Forge == "putuan,456"
                && cave.Buffs["buff-gushen"] == start.AddDays(2).Ticks && cave.HasGear("jianwan")
                && cave.Grants.Contains("dtin-shuiyue-lingsui-7") && cave.ImportSeq == 7 && cave.CountOf("shuiyue-lingsui") == 2,
                "expansion preserves in-flight production, buffs, gears and transfer accounting");
            Check(!Expand(cave, 1) && cave.Level == 2 && cave.CountOf("linggu") == 16 && cave.CountOf("ningshen") == 8,
                "replaying an old-level click cannot charge again or skip a level");
            Check(cave.Plant(2, "ningshen", start) && !cave.Plant(3, "linggu", start), "only the newly unlocked third field becomes plantable");
            Check(Expand(cave, 2) && cave.Level == 3 && cave.UnlockedFields == 4
                && cave.CountOf("linggu") == 8 && cave.CountOf("ningshen") == 4 && cave.CountOf("jianxin") == 2,
                "the second expansion charges its full distinct recipe and opens the fourth field");
            Check(!Expand(cave, 3) && cave.Level == 3 && cave.CountOf("linggu") == 8 && cave.CountOf("ningshen") == 4 && cave.CountOf("jianxin") == 2,
                "a max-level cave refuses further payment and expansion");
            Check(cave.LastNotice.Contains("满级"), "max-level refusal explains the reached cap");
            string archive = Path.Combine(root, "core", "dongtian-v1.txt"); cave.Save(archive);
            DongtianProgress back = DongtianProgress.Load(archive);
            Check(back.Level == 3 && back.UnlockedFields == 4 && back.CountOf("linggu") == 8 && back.CountOf("ningshen") == 4 && back.CountOf("jianxin") == 2
                && back.Fields[0] == field && back.Fields[2] != null && back.Grants.Contains("dtin-shuiyue-lingsui-7") && back.ImportSeq == 7,
                "archive reload preserves the paid expansion, field production and transfer ledger");
            Check(!Expand(back, 2) && back.CountOf("linggu") == 8 && back.Level == 3, "a stale request remains harmless after restart");
            back.Level = 4; back.Save(archive); DongtianProgress legacy = DongtianProgress.Load(archive);
            Check(legacy.Level == 4 && legacy.UnlockedFields == 4 && !Expand(legacy, 4) && legacy.CountOf("linggu") == 8,
                "historical level-four archives retain their fields without receiving a reward or downgrade");

            DongtianProgress natural = new DongtianProgress();
            natural.Plant(0, "linggu", start); natural.Plant(1, "ningshen", start);
            natural.Harvest(0, start.AddHours(6)); natural.Harvest(1, start.AddHours(6));
            natural.Plant(0, "linggu", start.AddHours(6)); natural.Harvest(0, start.AddHours(8));
            Check(Expand(natural, 1) && natural.Level == 2 && natural.Bag.Count == 0,
                "ordinary timed harvests supply a complete first expansion without new currency or grants");
            DongtianProgress competing = new DongtianProgress(); competing.Bag["linggu"] = 4; competing.Bag["ningshen"] = 2;
            Check(competing.StartPill("juqi", start) && !Expand(competing, 1) && competing.Level == 1
                && competing.CountOf("linggu") == 2 && competing.CountOf("ningshen") == 2 && competing.Furnace != null,
                "alchemy and expansion share the real bag, so a prior recipe prevents a double spend");
            Console.WriteLine(DongtianProgress.SelfTest());
        }
        private static void FormChecks(string assets, string root)
        {
            using (Form owner = new Form { ShowInTaskbar = false, Location = new Point(-25000, -25000), StartPosition = FormStartPosition.Manual })
            using (AdventureController controller = new AdventureController(owner, assets, false, null, false, Path.Combine(root, "profile")))
            {
                Field<Timer>(controller, "timer").Stop();
                controller.Cultivation.Mortal = false; controller.Cultivation.RealmIndex = 1; Stock(controller.Dongtian);
                using (DongtianForm panel = new DongtianForm(controller))
                {
                    Field<Timer>(panel, "refresh").Stop(); panel.RefreshData();
                    Button first = ExpandButton(panel);
                    Check(first != null, "an unlocked cave panel exposes a real expansion action");
                    if (first == null) return;
                    Check(Field<Label>(panel, "status").Text.Contains("灵谷×4") && Field<Label>(panel, "status").Text.Contains("凝神花×2"),
                        "the panel shows the next expansion's actual materials before payment");
                    Click(first);
                    DongtianProgress persisted = DongtianProgress.Load(controller.DongtianWritePath);
                    Check(controller.Dongtian.Level == 2 && persisted.Level == 2 && persisted.CountOf("linggu") == 16
                        && persisted.CountOf("ningshen") == 8 && !controller.DongtianPersistPending,
                        "clicking expansion follows the controller save path and persists one paid level");
                    Click(first);
                    Check(controller.Dongtian.Level == 2 && controller.Dongtian.CountOf("linggu") == 16,
                        "a queued duplicate event from the old button cannot pay a second recipe");
                    Button second = ExpandButton(panel);
                    using (FileStream locked = new FileStream(controller.DongtianWritePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        Click(second);
                        string pendingText = Field<Label>(panel, "notice").Text;
                        Check(controller.Dongtian.Level == 3 && controller.Dongtian.CountOf("linggu") == 8
                            && controller.DongtianPersistPending && pendingText.Contains("待落盘") && !pendingText.Contains("提交成功"),
                            "a locked archive reports pending persistence and never claims the expansion committed");
                        Click(second); controller.TryFlushDongtianPersistence();
                        Check(controller.Dongtian.Level == 3 && controller.Dongtian.CountOf("linggu") == 8 && controller.DongtianPersistPending,
                            "duplicate events and failed retries never charge a pending expansion again");
                    }
                    controller.TryFlushDongtianPersistence(); panel.RefreshData(); persisted = DongtianProgress.Load(controller.DongtianWritePath);
                    Check(!controller.DongtianPersistPending && persisted.Level == 3 && persisted.CountOf("linggu") == 8
                        && persisted.CountOf("ningshen") == 4 && persisted.CountOf("jianxin") == 2,
                        "unlocking the same archive lets the existing retry commit the full expansion exactly once");
                    Check(ExpandButton(panel) == null && Field<Label>(panel, "status").Text.Contains("满级"),
                        "the capped panel retires expansion and states its maximum capacity");
                    Check(!owner.Visible && !panel.Visible && !Field<KnightLayeredWindow>(controller, "heroWindow").Visible,
                        "all fixtures stay hidden and never render desktop actors");
                }
            }
        }
        private static void PendingGateChecks(string assets, string root)
        {
            using (Form owner = new Form { ShowInTaskbar = false })
            using (AdventureController controller = new AdventureController(owner, assets, false, null, false, Path.Combine(root, "pending-profile")))
            {
                Field<Timer>(controller, "timer").Stop();
                controller.Cultivation.Mortal = false; controller.Cultivation.RealmIndex = 1; Stock(controller.Dongtian);
                controller.Dongtian.Save(controller.DongtianWritePath); // Establish the pre-click fixture, never a live archive.
                using (DongtianForm panel = new DongtianForm(controller))
                {
                    Field<Timer>(panel, "refresh").Stop(); panel.RefreshData();
                    using (FileStream locked = new FileStream(controller.DongtianWritePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        Click(ExpandButton(panel));
                        Check(controller.Dongtian.Level == 2 && controller.DongtianPersistPending && controller.Dongtian.CountOf("linggu") == 16,
                            "a first paid expansion remains visibly pending when its actual archive is locked");
                        Click(ExpandButton(panel));
                        Check(controller.Dongtian.Level == 2 && controller.Dongtian.CountOf("linggu") == 16 && controller.Dongtian.CountOf("ningshen") == 8,
                            "even the new next-level button refuses another payment while the prior save is pending");
                        Check(Field<Label>(panel, "notice").Text.Contains("待落盘"), "a blocked pending operation keeps its persistence warning visible");
                    }
                    DongtianProgress old = DongtianProgress.Load(controller.DongtianWritePath);
                    Check(old.Level == 1 && old.CountOf("linggu") == 20 && old.CountOf("ningshen") == 10,
                        "before retry the complete previous archive has neither a half debit nor a free level");
                    controller.TryFlushDongtianPersistence();
                    DongtianProgress committed = DongtianProgress.Load(controller.DongtianWritePath);
                    Check(!controller.DongtianPersistPending && committed.Level == 2 && committed.CountOf("linggu") == 16 && committed.CountOf("ningshen") == 8,
                        "the original persistence retry commits exactly the accepted first expansion");
                }
            }
        }
        [STAThread] private static int Main(string[] args)
        {
            try
            {
                string root = Path.Combine(Path.GetFullPath(args[1]), "fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
                Console.WriteLine("Fixture root: " + root);
                CoreChecks(root);
                if (expand != null && args.Length < 3)
                {
                    FormChecks(Path.GetFullPath(args[0]), root);
                    PendingGateChecks(Path.GetFullPath(args[0]), root);
                }
                Console.WriteLine("Dongtian expansion: " + checks + " checks, " + failures + " failures.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
    }
}
