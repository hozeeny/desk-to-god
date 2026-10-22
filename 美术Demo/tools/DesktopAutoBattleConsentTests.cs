using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace NetBunny
{
    // Real planner/strike/settlement and consent methods on an unconstructed
    // controller: no Form, timer, native observer, profile or desktop actor.
    internal static class DesktopAutoBattleConsentTests
    {
        static int checks, failures;
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Set(object instance, string field, object value) { instance.GetType().GetField(field, Private).SetValue(instance, value); }
        static T Get<T>(object instance, string field) { return (T)instance.GetType().GetField(field, Private).GetValue(instance); }
        static object Call(object instance, string method, params object[] args) { return instance.GetType().GetMethod(method, Private).Invoke(instance, args); }
        static void Check(bool okay, string message) { checks++; Console.WriteLine((okay ? "PASS " : "FAIL ") + message); if (!okay) failures++; }

        static AdventureController Controller(bool desktop, bool virtualOnly)
        {
            AdventureController game = (AdventureController)FormatterServices.GetUninitializedObject(typeof(AdventureController));
            Set(game, "isolatedProfileRoot", desktop ? "in-memory-no-profile" : null);
            Set(game, "desktopEnvironment", new DesktopEnvironmentPolicy(virtualOnly));
            Set(game, "battle", new AdventureBattle(new AdventureProgress(), new Random(4)));
            Set(game, "cultivation", new CultivationProgress { Mortal = false });
            Set(game, "encounters", new CultivationEncounterScheduler(new Random(5)));
            Set(game, "consent", new MaintenanceConsentStore());
            Set(game, "desktopRecentEvents", new Queue<string>());
            Set(game, "pendingCleanup", new Queue<AdventureEnemy>());
            Set(game, "clock", Stopwatch.StartNew());
            Set(game, "cancellation", new CancellationTokenSource());
            Set(game, "demoStarted", -1.0);
            // The existing demonstration guards suppress archive writes; the
            // art host's hidden guard suppresses every render before window use.
            Set(game, "demonstrationOnly", true);
            Set(game, "artDemoRuntime", true);
            Set(game, "artDemoHostHidden", true);
            Set(game, "enabled", true);
            Set(game, "simulatedCleaner", (Func<CleanupBatch, CancellationToken, Task<CleanupResult>>)delegate { throw new InvalidOperationException("Unauthorized file worker was invoked."); });
            Set(game, "simulatedMemoryReclaimer", (Func<CancellationToken, Task<MemoryReclaimResult>>)delegate { throw new InvalidOperationException("Unauthorized memory worker was invoked."); });
            return game;
        }

        static AdventureEnemy Enemy(string id, bool memory)
        {
            return new AdventureEnemy
            {
                Id = id, IsBoss = memory, MonsterKind = "imp", Health = 1, MaxHealth = 1,
                Batch = memory ? null : new CleanupBatch(new List<CleanupCandidate> { new CleanupCandidate { Path = "C:\\InMemoryFixtureOnly\\untouched.tmp", Length = 1024 } }, 1)
            };
        }

        static AdventureEnemy Plan(AdventureController game)
        { return game.Battle.PlanNext(0, 0, enemy => (bool)Call(game, "AutoMaintenanceEligibility", enemy)); }

        static void SettleWithoutMaintenance(AdventureController game, AdventureEnemy enemy, string scenario)
        {
            Check(!(bool)Call(game, "DesktopCanMaintain", enemy), scenario + ": the real maintenance gate remains closed");
            int experience = game.Battle.Progress.Experience;
            long qi = game.Cultivation.LifetimeQi;
            List<AdventureEnemy> killed = game.Battle.Strike(enemy);
            Check(killed.Count == 1 && enemy.FellInCombat, scenario + ": the real automatic strike defeats its selected target");
            Call(game, "BeginCleanup", enemy);
            Check(enemy.Defeated && !enemy.Cleaning && !enemy.Authorized && game.Battle.Target == null && game.Battle.QueueCount == 0,
                scenario + ": virtual settlement retires the target and clears its combat queue");
            Check(game.Battle.Progress.Experience > experience && game.Cultivation.LifetimeQi > qi && game.Battle.Progress.FreedBytes == 0,
                scenario + ": combat grants experience and qi without inventing freed disk bytes");
            Check(Get<Task<CleanupResult>>(game, "cleanupTask") == null && Get<Task<MemoryReclaimResult>>(game, "memoryReclaimTask") == null
                && Get<Queue<AdventureEnemy>>(game, "pendingCleanup").Count == 0 && !enemy.CleanupStarted,
                scenario + ": no real maintenance worker or pending cleanup is created");
            Check(game.Battle.LastReward.Contains("未授权") && !game.Battle.LastReward.Contains("实际清理"),
                scenario + ": settlement does not claim successful system maintenance");
        }

        static void DesktopChecks(bool memory)
        {
            string kind = memory ? "memory boss" : "file monster";
            AdventureController game = Controller(true, false);
            Check(memory ? game.SetMemoryConsent(true) : game.SetCleanupConsent(true), kind + ": the attended-only scope can be enabled in memory");
            game.SetAutoPlan(true);
            AdventureEnemy first = Enemy("attended-only", memory); game.Battle.Enemies.Add(first);
            Check(Plan(game) == first && first.Authorization == AdventureAuthorization.Automatic, kind + ": attended-only consent must not block automatic combat selection");
            if (game.Battle.Target == first) SettleWithoutMaintenance(game, first, kind + " attended-only");
            Check(!game.MaintenanceConsentAutoMaintenance && (memory ? game.MaintenanceConsentMemory : game.MaintenanceConsentCleanup), kind + ": fighting cannot widen the independent consent flags");

            game = Controller(true, false);
            Check(game.SetAutoMaintenanceConsent(true), kind + ": the automatic scope can be enabled without granting attended scopes");
            game.SetAutoPlan(true); first = Enemy("no-scope", memory); game.Battle.Enemies.Add(first);
            Check(Plan(game) == first, kind + ": missing attended scope still allows pure combat");
            if (game.Battle.Target == first) SettleWithoutMaintenance(game, first, kind + " no attended scope");
            Check(!game.MaintenanceConsentCleanup && !game.MaintenanceConsentMemory, kind + ": automatic consent and combat do not grant an attended scope");

            game = Controller(true, false);
            if (memory) game.SetMemoryConsent(true); else game.SetCleanupConsent(true);
            game.SetAutoMaintenanceConsent(true); game.SetAutoPlan(true);
            first = Enemy("before-revoke", memory); game.Battle.Enemies.Add(first);
            Check(Plan(game) == first && (bool)Call(game, "DesktopCanMaintain", first), kind + ": both explicit scopes retain the existing maintenance permission");
            Check(game.SetAutoMaintenanceConsent(false) && game.AutoPlanning && game.Battle.Target == null, kind + ": revocation cancels the old automatic order while preserving automatic combat preference");
            Check(Plan(game) == first && first.Authorization == AdventureAuthorization.Automatic, kind + ": a revoked automatic-maintenance scope cannot strand the next combat selection");
            if (game.Battle.Target == first) SettleWithoutMaintenance(game, first, kind + " after revocation");
        }

        static void ProcessAndLegacyChecks()
        {
            foreach (bool desktop in new[] { false, true })
                foreach (bool granted in new[] { false, true })
                    foreach (bool unattended in new[] { false, true })
                    {
                        AdventureController game = Controller(desktop, false);
                        MaintenanceConsentStore consent = Get<MaintenanceConsentStore>(game, "consent");
                        consent.CleanupEnabled = consent.MemoryEnabled = granted; consent.AutoMaintenanceEnabled = unattended;
                        foreach (bool memory in new[] { false, true })
                            Check((bool)Call(game, "AutoMaintenanceEligibility", Enemy("matrix", memory)) == (desktop || !granted || unattended),
                                "selection matrix desktop=" + desktop + ", granted=" + granted + ", automatic-consent=" + unattended + ", memory=" + memory);
                        AdventureEnemy program = Enemy("program", false); program.Batch = null;
                        program.ProcessTarget = new ProcessIdentity { Id = 4321, StartedUtcTicks = 123456, ExecutablePath = "C:\\InMemoryFixtureOnly\\not-running.exe", Name = "memory fixture" };
                        game.Battle.Enemies.Add(program); game.Battle.SetAutomatic(true);
                        Check(!(bool)Call(game, "AutoMaintenanceEligibility", program) && Plan(game) == null, "automatic planning never selects a program target, regardless of entry or maintenance grants");
                    }
            AdventureController observed = Controller(true, false);
            AdventureEnemy automaticProgram = Enemy("program-fallback", false); automaticProgram.Batch = null;
            automaticProgram.ProcessTarget = new ProcessIdentity { Id = 4321, StartedUtcTicks = 123456, ExecutablePath = "C:\\InMemoryFixtureOnly\\not-running.exe", Name = "memory fixture" };
            observed.Battle.Enemies.Add(automaticProgram); observed.Battle.SetAutomatic(true);
            // Defense in depth if a stale automatic program order somehow exists.
            observed.Battle.PlanNext(0, 0); observed.Battle.Strike(automaticProgram);
            bool asked = false; observed.DesktopProcessConfirmation = delegate { asked = true; throw new InvalidOperationException("No confirmation is allowed for automatic program orders."); };
            Call(observed, "BeginCleanup", automaticProgram);
            Check(!asked && !observed.DesktopProcessBusy && automaticProgram.Defeated && observed.Battle.LastReward.Contains("虚拟奖励"), "an accidental automatic program order settles combat without confirmation, process requests or a false exit result");

            AdventureController virtualGame = Controller(true, true);
            Check(!virtualGame.SetCleanupConsent(true) && !virtualGame.SetMemoryConsent(true) && !virtualGame.SetAutoMaintenanceConsent(true), "virtual entry still refuses all real-maintenance grants");
            virtualGame.SetAutoPlan(true); AdventureEnemy virtualBound = Enemy("virtual-ceiling", false); virtualGame.Battle.Enemies.Add(virtualBound);
            Check(Plan(virtualGame) == virtualBound, "the virtual entry can still settle a defensive file-backed combat fixture without gaining maintenance authority");
            if (virtualGame.Battle.Target == virtualBound) SettleWithoutMaintenance(virtualGame, virtualBound, "virtual entry");
        }

        static int Main()
        {
            try { DesktopChecks(false); DesktopChecks(true); ProcessAndLegacyChecks(); Console.WriteLine("Desktop automatic combat consent: " + checks + " checks, " + failures + " failures. No forms, observers, processes, maintenance, saves or natural-time advancement."); return failures == 0 ? 0 : 1; }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
    }
}
