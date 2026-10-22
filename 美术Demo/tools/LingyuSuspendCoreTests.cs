using System;
using System.IO;
using System.Text;

namespace NetBunny
{
    // Only real core engines and private archive fixtures; never constructs GUI or probes the system.
    internal static class LingyuSuspendCoreTests
    {
        private static int checks, failures;
        private sealed class FixedRandom : Random
        {
            internal double Roll;
            internal int Calls;
            internal FixedRandom(double roll) { Roll = roll; }
            public override double NextDouble() { Calls++; return Roll; }
        }
        private static void Check(bool okay, string message)
        {
            checks++;
            Console.WriteLine((okay ? "PASS " : "FAIL ") + message);
            if (!okay) failures++;
        }
        private static LingyuProgress Progress(string node)
        {
            LingyuProgress p = new LingyuProgress { ExpeditionActive = true, ExpeditionNode = node, ExpeditionDepth = 3 };
            p.RealmEntered.Add("qianmian"); p.ExpeditionFlags.Add("route-kept");
            return p;
        }
        private static JiyuanContext Context(LingyuProgress p, bool wandering)
        {
            BaiyeProgress baiye = new BaiyeProgress();
            if (wandering) baiye.AddYun("youli", 60, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            return new JiyuanContext { SpiritRoot = "fire", RealmIndex = 0, Layer = 12, MirageVisits = p.Visits("qianmian"), Baiye = baiye, Lingyu = p };
        }
        private static int Material(LingyuProgress p, string id) { int n; return p.Materials.TryGetValue(id, out n) ? n : 0; }
        private static long MaterialTotal(LingyuProgress p) { long n = 0; foreach (int amount in p.Materials.Values) n += amount; return n; }
        private static long QiTotal(LingyuProgress p) { long n = 0; foreach (long amount in p.PendingQi.Values) n += amount; return n; }
        private static bool Unpaid(LingyuProgress p) { return p.Grants.Count == 0 && p.PendingQi.Count == 0 && p.Materials.Count == 0 && p.Scrolls.Count == 0 && p.Heritages.Count == 0; }
        private static bool Offer(MirageEngine e, string id) { return e.PendingJiyuan != null && e.PendingJiyuan.Id == id; }

        private static void CombatRoundTrip(string root, string node, string outcome)
        {
            LingyuProgress p = Progress(node); p.CombatPending = true; p.CombatOutcome = outcome;
            if (outcome == "jiyuan:jing-wen-1") p.JiyuanChoices["jing-wen-1"] = 1;
            MirageEngine e = new MirageEngine(p, new FixedRandom(1));
            e.Suspend(); int pausedLog = p.ExpeditionLog.Count; e.Suspend();
            Check(!p.ExpeditionActive && p.CombatPending && p.CombatOutcome == outcome, "suspending retains pending combat semantics: " + outcome);
            Check(p.ExpeditionLog.Count == pausedLog, "duplicate suspension does not repeat the log: " + outcome);
            Check(Unpaid(p) && !e.ResumePendingCombat(), "suspension neither rewards nor resumes the fight: " + outcome);
            string file = Path.Combine(root, outcome.Replace(':', '-') + ".txt"); p.Save(file);
            LingyuProgress loaded = LingyuProgress.Load(file);
            Check(!loaded.ExpeditionActive && loaded.CombatPending && loaded.CombatOutcome == outcome
                && loaded.ExpeditionNode == node && loaded.ExpeditionDepth == 3 && loaded.ExpeditionFlags.Contains("route-kept"),
                "inactive archive reload preserves the complete pending route: " + outcome);
            MirageEngine back = new MirageEngine(loaded, new FixedRandom(1)); int requests = 0; bool boss = false;
            back.CombatRequested += delegate(string label, bool isBoss) { requests++; boss = isBoss; };
            Check(!back.ResumePendingCombat() && requests == 0, "reloaded inactive combat waits for explicit entry: " + outcome);
            JiyuanContext ctx = Context(loaded, false); back.Enter(ctx);
            Check(requests == 1 && boss == (outcome == "final") && loaded.CombatOutcome == outcome,
                "re-entry reissues the original ordinary, lord or chosen-event fight: " + outcome);
            back.Enter(ctx); back.ResumePendingCombat();
            Check(Unpaid(loaded), "repeated entry and resume requests do not grant a reward: " + outcome);
            back.OnCombatWon(ctx);
            bool paid = outcome == "final" ? loaded.Visits("qianmian") == 1 && loaded.Grants.ContainsKey("mirage-clear-1")
                : outcome == "jiyuan:jing-wen-1" ? Material(loaded, "suijing-jing") == 1 && loaded.PendingQi.ContainsKey("jiyuan-jing-wen-1")
                : loaded.ExpeditionFlags.Contains("corridor-cleared") && Material(loaded, "jinghua-shuiyue") == 1;
            Check(paid && !loaded.CombatPending, "resumed victory grants the original reward: " + outcome);
            int grants = loaded.Grants.Count, visits = loaded.Visits("qianmian"); long qi = QiTotal(loaded), stock = MaterialTotal(loaded);
            back.OnCombatWon(ctx); back.OnCombatFailed(ctx); back.Enter(ctx);
            Check(!back.ResumePendingCombat() && loaded.Grants.Count == grants && QiTotal(loaded) == qi
                && MaterialTotal(loaded) == stock && loaded.Visits("qianmian") == visits,
                "duplicate settlement, entry and resume never duplicate the reward: " + outcome);
        }

        private static void InactiveCallbacks()
        {
            LingyuProgress p = Progress("atrium"); p.CombatPending = true; p.CombatOutcome = "final";
            MirageEngine e = new MirageEngine(p, new FixedRandom(1)); e.Suspend();
            e.OnCombatWon(Context(p, false));
            Check(p.CombatPending && p.CombatOutcome == "final" && Unpaid(p), "a stale victory callback cannot settle suspended combat");
            e.OnCombatFailed(Context(p, false));
            Check(p.CombatPending && p.CombatOutcome == "final" && Unpaid(p), "a stale defeat callback cannot discard suspended combat");
        }

        private static void OfferRoundTrip(string root, string node, string id, bool suspend)
        {
            LingyuProgress p = Progress(node); FixedRandom random = new FixedRandom(0);
            MirageEngine e = new MirageEngine(p, random); JiyuanContext ctx = Context(p, true); e.Enter(ctx);
            Check(Offer(e, id), "the original encounter is genuinely offered: " + id + " suspend=" + suspend);
            if (!Offer(e, id)) return;
            string prompt = e.NodeText(); int options = e.CurrentChoices().Length;
            if (suspend) e.Suspend();
            string file = Path.Combine(root, id + "-" + suspend + ".txt"); p.Save(file);
            LingyuProgress loaded = LingyuProgress.Load(file); FixedRandom rejecting = new FixedRandom(1);
            MirageEngine back = new MirageEngine(loaded, rejecting); JiyuanContext after = Context(loaded, true);
            back.Enter(after);
            Check(Offer(back, id) && back.NodeText() == prompt && back.CurrentChoices().Length == options,
                "archive re-entry restores the same pending offer and choices: " + id + " suspend=" + suspend);
            Check(rejecting.Calls == 0, "restoring an existing offer consumes no random draws: " + id + " suspend=" + suspend);
            back.Enter(after);
            Check(Offer(back, id) && rejecting.Calls == 0 && Unpaid(loaded), "repeated entry preserves the same unpaid offer: " + id);
            back.Choose(99, after);
            Check(Offer(back, id) && Unpaid(loaded), "an invalid choice cannot erase the pending offer: " + id);
            back.Suspend(); int log = loaded.ExpeditionLog.Count; back.Suspend(); back.Choose(0, after);
            Check(loaded.ExpeditionLog.Count == log && !loaded.JiyuanChoices.ContainsKey(id) && Unpaid(loaded),
                "a suspended offer remains undecided and repeated suspension is inert: " + id);
            back.Enter(after); back.Choose(0, after);
            Check(loaded.JiyuanChoices.ContainsKey(id) && loaded.Grants.ContainsKey("jiyuan-" + id)
                && (id == "jing-mian-hua" ? Material(loaded, "jinghua") == 1 : loaded.PendingQi["jiyuan-jing-wen-1"] == 30),
                "the restored offer can be chosen through the unchanged reward ledger: " + id);
            loaded.Save(file); LingyuProgress settled = LingyuProgress.Load(file);
            MirageEngine final = new MirageEngine(settled, new FixedRandom(1)); final.Enter(Context(settled, true));
            Check(!Offer(final, id) && settled.Grants.Count == loaded.Grants.Count && settled.JiyuanChoices.ContainsKey(id),
                "a consumed offer stays consumed after another restart: " + id);
        }

        private static void ArchiveValidation(string root)
        {
            string basic = "version=1\nexpedition=0\nnode=corridor\nentered=qianmian\ndepth=3\n";
            string file = Path.Combine(root, "validation.txt");
            Action<string, string> rejected = delegate(string content, string label)
            {
                File.WriteAllText(file, content, new UTF8Encoding(false));
                LingyuProgress p = LingyuProgress.Load(file); MirageEngine e = new MirageEngine(p, new FixedRandom(1));
                Check(e.PendingJiyuan == null && p.ExpeditionDepth == 3, "archive discards only the invalid pending offer: " + label);
            };
            rejected(basic + "pendingjiyuan=unknown-encounter\n", "unknown id");
            rejected(basic + "pendingjiyuan=jing-mian-hua|bad\n", "malformed token");
            rejected(basic + "pendingjiyuan=jing-wen-1\n", "wrong node");
            rejected(basic.Replace("node=corridor", "node=not-a-node") + "pendingjiyuan=jing-mian-hua\n", "unknown expedition node");
            rejected(basic.Replace("entered=qianmian\n", "") + "pendingjiyuan=jing-mian-hua\n", "realm never entered");
            rejected(basic + "combatpending=1\noutcome=corridor-cleared\npendingjiyuan=jing-mian-hua\n", "combat and offer conflict");
            rejected(basic + "pendingjiyuan=jing-mian-hua\njiyuan.jing-mian-hua=0\n", "choice already recorded");
            rejected(basic + "pendingjiyuan=jing-mian-hua\ngrant.jiyuan-jing-mian-hua=123\n", "reward already granted");
            File.WriteAllText(file, basic + "pendingjiyuan=jing-mian-hua\n", new UTF8Encoding(false));
            LingyuProgress valid = LingyuProgress.Load(file);
            Check(Offer(new MirageEngine(valid, new FixedRandom(1)), "jing-mian-hua") && !valid.ExpeditionActive,
                "a valid inactive offer is available to its engine before re-entry");
            File.WriteAllText(file, basic, new UTF8Encoding(false)); LingyuProgress legacy = LingyuProgress.Load(file);
            Check(!legacy.ExpeditionActive && legacy.ExpeditionNode == "corridor" && legacy.ExpeditionDepth == 3
                && new MirageEngine(legacy, new FixedRandom(1)).PendingJiyuan == null,
                "old version-one archives without the optional field remain compatible");
        }

        private static int Main(string[] args)
        {
            try
            {
                string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
                Console.WriteLine("Private fixture root: " + root);
                CombatRoundTrip(root, "corridor", "corridor-cleared");
                CombatRoundTrip(root, "atrium", "final");
                CombatRoundTrip(root, "echo", "jiyuan:jing-wen-1");
                InactiveCallbacks();
                OfferRoundTrip(root, "corridor", "jing-mian-hua", true);
                OfferRoundTrip(root, "corridor", "jing-mian-hua", false);
                OfferRoundTrip(root, "echo", "jing-wen-1", true);
                ArchiveValidation(root);
                Console.WriteLine(LingyuSelfTest.Run());
                Console.WriteLine("Lingyu suspension core: " + checks + " checks, " + failures + " failures. Pure logic/private archives; no GUI, live profiles or system probes.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
    }
}
