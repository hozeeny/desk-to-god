using System;

namespace NetBunny
{
    // Pure expedition logic only: no controller, forms, native probes or live archives.
    internal static class LingyuRealmGateTests
    {
        private static int checks, failures;

        private static void Check(bool okay, string message)
        {
            checks++;
            Console.WriteLine((okay ? "PASS " : "FAIL ") + message);
            if (!okay) failures++;
        }

        private static JiyuanContext Context(int realm, int layer, bool prior, int visits, int priorChoice)
        {
            LingyuProgress progress = new LingyuProgress();
            if (prior) progress.JiyuanChoices["jing-wen-1"] = priorChoice;
            return new JiyuanContext
            {
                RealmIndex = realm, Layer = layer, MirageVisits = visits,
                SpiritRoot = "fire", Baiye = new BaiyeProgress(), Lingyu = progress
            };
        }

        private static bool OffersSequel(JiyuanContext context)
        {
            JiyuanDef found = JiyuanEngine.Evaluate("atrium", context, null);
            return found != null && found.Id == "jing-wen-2";
        }

        private static void GateChecks()
        {
            Check(!OffersSequel(Context(0, 11, true, 1, 0)), "qi layer 11 remains below the sequel cultivation gate");
            Check(OffersSequel(Context(0, 12, true, 1, 0)), "qi layer 12 unlocks the sequel after its prior story and first clear");
            Check(OffersSequel(Context(1, 1, true, 1, 0)), "zhuji layer 1 retains access after cultivation layers reset");
            Check(OffersSequel(Context(2, 1, true, 1, 0)), "jindan layer 1 exceeds the qi layer 12 gate");
            Check(OffersSequel(Context(6, 1, true, 1, 0)), "dujie layer 1 exceeds the qi layer 12 gate");
            Check(OffersSequel(Context(7, 1, true, 1, 0)), "the final realm retains access to the missed story");
            Check(!OffersSequel(Context(1, 1, false, 1, 0)), "a higher realm cannot bypass the prior story");
            Check(!OffersSequel(Context(1, 1, true, 0, 0)), "a higher realm cannot bypass the first-clear requirement");
            Check(!OffersSequel(Context(0, 12, false, 1, 0)), "the original qi gate also keeps the prior-story requirement");
            Check(!OffersSequel(Context(0, 12, true, 0, 0)), "the original qi gate also keeps the first-clear requirement");
            JiyuanContext completed = Context(1, 1, true, 1, 0);
            completed.Lingyu.JiyuanChoices["jing-wen-2"] = 0;
            Check(!OffersSequel(completed), "an already completed sequel never becomes replayable after breakthrough");
        }

        private static void SequelRewardChecks(int priorChoice)
        {
            JiyuanContext context = Context(1, 1, true, 1, priorChoice);
            context.Lingyu.ExpeditionNode = "atrium";
            MirageEngine engine = new MirageEngine(context.Lingyu, new Random(29));
            engine.Enter(context);
            bool offered = engine.PendingJiyuan != null && engine.PendingJiyuan.Id == "jing-wen-2";
            Check(offered, "returning to the atrium after zhuji presents prior-choice branch " + priorChoice);
            if (!offered) return; // A failed gate must never accidentally initiate the boss instead.
            engine.Choose(0, context);
            if (priorChoice == 0)
                Check(context.Lingyu.Scrolls.ContainsKey("jingxin-scroll") && context.Lingyu.Scrolls["jingxin-scroll"] == 1
                    && context.Lingyu.PendingQi.ContainsKey("jiyuan-jing-wen-2") && context.Lingyu.PendingQi["jiyuan-jing-wen-2"] == 60,
                    "the calm branch still grants one mirror-heart scroll and 60 qi after zhuji");
            else
                Check(context.Lingyu.Materials.ContainsKey("suijing-jing") && context.Lingyu.Materials["suijing-jing"] == 2
                    && context.Lingyu.PendingQi.ContainsKey("jiyuan-jing-wen-2") && context.Lingyu.PendingQi["jiyuan-jing-wen-2"] == 45,
                    "the broken-mirror branch still grants two crystals and 45 qi after zhuji");
            Check(context.Lingyu.Grants.ContainsKey("jiyuan-jing-wen-2") && !OffersSequel(context),
                "the higher-realm sequel records its existing one-time reward ledger");
        }

        private static int Main()
        {
            try
            {
                GateChecks();
                SequelRewardChecks(0);
                SequelRewardChecks(1);
                Console.WriteLine(LingyuSelfTest.Run());
                Console.WriteLine("Lingyu realm gates: " + checks + " checks, " + failures + " failures. Pure logic only; no GUI, natural archives or natural-time advancement.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
    }
}
