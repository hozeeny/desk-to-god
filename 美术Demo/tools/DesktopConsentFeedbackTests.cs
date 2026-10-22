using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace NetBunny
{
    // Actual consent storage + controller setters/view, without constructing the
    // controller's forms, observers, timers, workers or business archives.
    internal static class DesktopConsentFeedbackTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static int checks, failures;
        static void Set(object instance, string field, object value) { instance.GetType().GetField(field, Private).SetValue(instance, value); }
        static T Get<T>(object instance, string field) { return (T)instance.GetType().GetField(field, Private).GetValue(instance); }
        static object Call(object instance, string method, params object[] args) { return instance.GetType().GetMethod(method, Private).Invoke(instance, args); }
        static void Check(bool okay, string message) { checks++; Console.WriteLine((okay ? "PASS " : "FAIL ") + message); if (!okay) failures++; }
        static bool Change(AdventureController game, string scope, bool value)
        { return scope == "cleanup" ? game.SetCleanupConsent(value) : scope == "memory" ? game.SetMemoryConsent(value) : game.SetAutoMaintenanceConsent(value); }
        static bool Granted(AdventureController game, string scope)
        { return scope == "cleanup" ? game.MaintenanceConsentCleanup : scope == "memory" ? game.MaintenanceConsentMemory : game.MaintenanceConsentAutoMaintenance; }
        static string RetryField(string scope)
        { return scope == "cleanup" ? "consentRetryCleanup" : scope == "memory" ? "consentRetryMemory" : "consentRetryAutoMaint"; }

        static AdventureController Controller(string profile, string state)
        {
            Directory.CreateDirectory(profile);
            AdventureController game = (AdventureController)FormatterServices.GetUninitializedObject(typeof(AdventureController));
            Set(game, "isolatedProfileRoot", profile);
            Set(game, "consentPathOverride", Path.Combine(profile, "maintenance-consent.txt"));
            Set(game, "desktopEnvironment", new DesktopEnvironmentPolicy(false));
            Set(game, "consent", new MaintenanceConsentStore());
            Set(game, "battle", new AdventureBattle(new AdventureProgress()));
            Set(game, "cultivation", new CultivationProgress());
            Set(game, "clock", Stopwatch.StartNew());
            Set(game, "desktopRecentEvents", new Queue<string>());
            Set(game, "desktopMaintenance", new Queue<DesktopMaintenanceEntry>());
            Set(game, "pendingCleanup", new Queue<AdventureEnemy>());
            Set(game, "liveTemp", new LiveTempPopulation());
            Set(game, "lingyu", new LingyuProgress());
            Set(game, "dongtian", new DongtianProgress());
            Set(game, "networkState", new KnightState());
            Set(game, "desktopLastSaveSucceeded", true);
            Set(game, "desktopAcceptanceSpeed", 1);
            Set(game, "displayScale", 1f);
            Set(game, "demoStarted", -1.0);
            Set(game, "blockUntil", -1.0);
            Set(game, "blockCooldownUntil", -1.0);
            Set(game, "demonstrationOnly", true); // Save cannot write business archives.
            Call(game, "RecordDesktopEvent", "旧的出招消息 · 木芽小妖");
            if (state == "rest") { game.Battle.Progress.Dead = true; game.Battle.Progress.Health = 0; }
            if (state == "awaken") game.Cultivation.MortalKills = 10;
            return game;
        }

        static void NoWork(AdventureController game, string scenario)
        {
            Check(Get<Task<CleanupResult>>(game, "cleanupTask") == null && Get<Task<MemoryReclaimResult>>(game, "memoryReclaimTask") == null
                && !game.DesktopProcessBusy && Get<Queue<AdventureEnemy>>(game, "pendingCleanup").Count == 0,
                scenario + ": consent feedback starts no maintenance or process worker");
            Check(Get<object>(game, "tempWatcher") == null && Get<bool>(game, "memoryBusy") == false && Get<bool>(game, "diskBusy") == false,
                scenario + ": no system observer or sampling is started");
        }

        static void EnableFailure(string root, string scope, string state)
        {
            string scenario = scope + " enable failure during " + state;
            AdventureController game = Controller(Path.Combine(root, scope + "-" + state), state);
            string path = Get<string>(game, "consentPathOverride");
            Directory.CreateDirectory(path + ".new"); // Existing unwritable-consent fixture.
            Check(!Change(game, scope, true) && !Granted(game, scope), scenario + ": failed durable write leaves the requested grant closed");
            string notice = game.DesktopNotice;
            Console.WriteLine("OBSERVED " + scenario + ": " + notice.Replace("\n", " | "));
            Check(notice.Contains("授权") && notice.Contains("失败") && notice.Contains("关闭") && !notice.Contains("旧的出招消息"),
                scenario + ": the failure dialog source explains the failed grant instead of stale combat text");
            DesktopPlayerView view = game.ReadDesktopPlayerView();
            Check(view.Notice.Contains("授权") && view.Notice.Contains("失败") && view.RecentEvents.Any(e => e.Contains("授权") && e.Contains("失败")),
                scenario + ": player notice and recent events expose the actual consent failure");
            Call(game, "RecordDesktopEvent", "新的战斗反馈 · 不应覆盖授权失败");
            Check(game.DesktopNotice == notice, scenario + ": ordinary combat feedback cannot hide an unresolved consent failure");
            Check(!game.MaintenanceConsentCleanup && !game.MaintenanceConsentMemory && !game.MaintenanceConsentAutoMaintenance,
                scenario + ": feedback cannot open any other scope");
            Check(Get<bool>(game, "desktopLastSaveSucceeded"), scenario + ": consent failure does not change the independent business-save status");
            NoWork(game, scenario);
            Directory.Delete(path + ".new");
            Check(Change(game, scope, true) && Granted(game, scope), scenario + ": explicit retry succeeds after the private block is removed");
            Check(!game.DesktopNotice.Contains("写入失败"), scenario + ": a successful retry cannot leave the old failed-write dialog text active");
        }

        static void RevokeFailure(string root, string scope)
        {
            string scenario = scope + " revoke persistence failure";
            AdventureController game = Controller(Path.Combine(root, scope + "-revoke"), "normal");
            string path = Get<string>(game, "consentPathOverride");
            Check(Change(game, scope, true) && Granted(game, scope), scenario + ": the initial private grant is genuinely persisted");
            Directory.CreateDirectory(path + ".new");
            Directory.CreateDirectory(path + ".revoked");
            using (FileStream blocked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Check(!Change(game, scope, false) && !Granted(game, scope) && Get<bool>(game, RetryField(scope)),
                    scenario + ": local revocation closes immediately while persistence remains pending");
                string notice = game.DesktopNotice;
                Console.WriteLine("OBSERVED " + scenario + ": " + notice.Replace("\n", " | "));
                Check(notice.Contains("关闭") && notice.Contains("未能更新") && notice.Contains("重试") && !notice.Contains("旧的出招消息"),
                    scenario + ": the dialog reports local closure and pending persistence honestly");
                Check(!notice.Contains("手动删除") && !notice.Contains("maintenance-consent.txt"),
                    scenario + ": the desktop guidance does not send the player to delete a different archive");
                Check(game.ReadDesktopPlayerView().RecentEvents.Any(e => e.Contains("未能更新") && e.Contains("重试")),
                    scenario + ": pending revocation remains inspectable in player events");
                NoWork(game, scenario);
            }
            Directory.Delete(path + ".new"); Directory.Delete(path + ".revoked");
            Check(Change(game, scope, false) && !Granted(game, scope) && !Get<bool>(game, RetryField(scope)),
                scenario + ": an explicit retry persists the closure without reopening the grant");
            Check(!game.DesktopNotice.Contains("未能更新"), scenario + ": a committed retry does not keep claiming persistence is pending");
        }

        static void PendingScopesRemainIndependent(string root)
        {
            AdventureController game = Controller(Path.Combine(root, "independent-pending-scopes"), "normal");
            string path = Get<string>(game, "consentPathOverride");
            Check(Change(game, "cleanup", true) && Change(game, "memory", true), "multiple pending scopes: the initial private grants exist");
            Directory.CreateDirectory(path + ".new"); Directory.CreateDirectory(path + ".revoked");
            using (FileStream blocked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(!Change(game, "cleanup", false) && !Change(game, "memory", false), "multiple pending scopes: both local revocations fail to persist");
            Directory.Delete(path + ".new"); Directory.Delete(path + ".revoked");
            Check(Change(game, "automatic", true), "multiple pending scopes: a different scope can explicitly succeed after the storage block clears");
            string notice = game.DesktopNotice;
            Check(notice.Contains("文件清理") && notice.Contains("内存维护") && notice.Contains("未能更新"),
                "multiple pending scopes: an unrelated successful action cannot hide either pending revocation");
            Check(Change(game, "cleanup", false), "multiple pending scopes: an explicit cleanup retry succeeds");
            notice = game.DesktopNotice;
            Check(!notice.Contains("文件清理授权已在本机立即关闭") && notice.Contains("内存维护") && notice.Contains("未能更新"),
                "multiple pending scopes: one successful retry clears only its own failure");
            Call(game, "TryFlushConsentRevocation");
            Check(!Get<bool>(game, "consentRetryMemory") && !game.DesktopNotice.Contains("未能更新"),
                "multiple pending scopes: background retry clears the final resolved failure");
            Check(Get<bool>(game, "desktopLastSaveSucceeded"), "multiple pending scopes: retry feedback leaves business-save status untouched");
            NoWork(game, "multiple pending scopes");
        }

        static void PendingReenableStaysHonest(string root, string scope)
        {
            string scenario = scope + " reenable while revocation is pending";
            AdventureController game = Controller(Path.Combine(root, scope + "-pending-reenable"), "normal");
            string path = Get<string>(game, "consentPathOverride");
            Check(Change(game, scope, true), scenario + ": the initial private grant exists");
            Directory.CreateDirectory(path + ".new"); Directory.CreateDirectory(path + ".revoked");
            using (FileStream blocked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(!Change(game, scope, false), scenario + ": local revocation fails to persist");
            Directory.Delete(path + ".new"); Directory.Delete(path + ".revoked");
            Check(Change(game, scope, true) && Granted(game, scope) && Get<bool>(game, RetryField(scope)),
                scenario + ": feedback preserves the existing grant and pending-retry semantics");
            string notice = game.DesktopNotice;
            Check(notice.Contains("未能更新") && notice.Contains("重试") && !notice.Contains("已开启") && !notice.Contains("保存成功"),
                scenario + ": the unresolved closure retry cannot be reported as an enabled or saved result");
            Call(game, "TryFlushConsentRevocation");
            Check(!Granted(game, scope) && !Get<bool>(game, RetryField(scope)) && !game.DesktopNotice.Contains("未能更新"),
                scenario + ": the existing retry closes the grant and clears the resolved UI failure");
            NoWork(game, scenario);
        }

        static int Main(string[] args)
        {
            try
            {
                string root = Path.Combine(Path.GetFullPath(args.Length > 1 ? args[1] : args[0]), "private-consent-fixtures");
                foreach (string scope in new[] { "cleanup", "memory", "automatic" })
                {
                    foreach (string state in new[] { "normal", "rest", "awaken" }) EnableFailure(root, scope, state);
                    RevokeFailure(root, scope);
                    PendingReenableStaysHonest(root, scope);
                }
                PendingScopesRemainIndependent(root);
                Console.WriteLine("Desktop consent feedback: " + checks + " checks, " + failures + " failures. Private consent files only; no forms, observers, maintenance workers, business archives or natural-time advancement.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
    }
}
