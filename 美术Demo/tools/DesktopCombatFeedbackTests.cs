using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace NetBunny
{
    // Invoke only combat/view methods on an in-memory controller. Bypassing its
    // desktop constructor deliberately creates no forms, actors, probes or saves.
    internal static class DesktopCombatFeedbackTests
    {
        static int checks, failures;
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Set(object instance, string field, object value) { instance.GetType().GetField(field, Private).SetValue(instance, value); }
        static object Call(object instance, string method, params object[] args) { return instance.GetType().GetMethod(method, Private).Invoke(instance, args); }
        static void Check(bool okay, string message) { checks++; Console.WriteLine((okay ? "PASS " : "FAIL ") + message); if (!okay) failures++; }

        static AdventureController Controller()
        {
            AdventureController game = (AdventureController)FormatterServices.GetUninitializedObject(typeof(AdventureController));
            Set(game, "isolatedProfileRoot", "in-memory-combat-fixture");
            Set(game, "desktopEnvironment", new DesktopEnvironmentPolicy(true));
            Set(game, "battle", new AdventureBattle(new AdventureProgress()));
            Set(game, "cultivation", new CultivationProgress());
            Set(game, "clock", Stopwatch.StartNew());
            Set(game, "desktopRecentEvents", new Queue<string>());
            Set(game, "desktopMaintenance", new Queue<DesktopMaintenanceEntry>());
            Set(game, "lingyu", new LingyuProgress());
            Set(game, "dongtian", new DongtianProgress());
            Set(game, "networkState", new KnightState());
            Set(game, "desktopLastSaveSucceeded", true);
            Set(game, "displayScale", 1f);
            Set(game, "demoStarted", -1.0);
            Set(game, "blockUntil", -1.0);
            Set(game, "blockCooldownUntil", -1.0);
            Set(game, "enabled", true);
            game.Battle.Progress.Health = 100;
            return game;
        }

        static AdventureEnemy Enemy(AdventureController game)
        {
            // Feet are 80 pixels from the hero, inside the unchanged 86-pixel reach.
            AdventureEnemy enemy = new AdventureEnemy { Id = "imp", IsVirtual = true, MonsterKind = "imp", MaxHealth = 144, Health = 144, X = 125, Y = 70 };
            game.Battle.Enemies.Add(enemy); game.Battle.Authorize(enemy); return enemy;
        }

        static void HitChecks()
        {
            AdventureController game = Controller(); AdventureEnemy enemy = Enemy(game);
            Call(game, "AttackHero", enemy, true, 2.6);
            DesktopPlayerView view = game.ReadDesktopPlayerView();
            Check(game.Battle.Progress.Health == 94 && enemy.AttackTime == 0, "ordinary hits retain their original six damage and attack reset");
            Check(view.Notice.Contains("受到6点伤害") && view.RecentEvents.Any(e => e.Contains("受到6点伤害")), "an actual incoming hit appears in both the notice and recent events");

            game = Controller(); enemy = Enemy(game); game.TryBlock();
            Call(game, "AttackHero", enemy, true, 2.6); view = game.ReadDesktopPlayerView();
            Check(game.Battle.Progress.Health == 99, "guard keeps its original one-point incoming hit");
            Check(view.Notice.Contains("格挡成功") && view.Notice.Contains("挡下5点") && view.Notice.Contains("实受1点"), "successful guard reports five prevented and one actually lost health");
            double guardUntil = (double)typeof(AdventureController).GetField("blockUntil", Private).GetValue(game);
            double cooldownUntil = (double)typeof(AdventureController).GetField("blockCooldownUntil", Private).GetValue(game);
            Check(Math.Abs(cooldownUntil - guardUntil - 9) < .00001 && !game.TryBlock(), "feedback preserves the one-second guard window and ten-second cooldown");

            game = Controller(); enemy = Enemy(game); game.Battle.CultivationDefenseFactor = .75; game.TryBlock();
            Call(game, "AttackHero", enemy, true, 2.6); view = game.ReadDesktopPlayerView();
            Check(game.Battle.Progress.Health == 99 && view.Notice.Contains("挡下3点") && view.Notice.Contains("实受1点"), "guard feedback uses actual realm defense rounding instead of raw incoming damage");

            game = Controller(); enemy = Enemy(game); game.Battle.Progress.Health = 2; game.TryBlock();
            Call(game, "AttackHero", enemy, true, 2.6); view = game.ReadDesktopPlayerView();
            Check(game.Battle.Progress.Health == 1 && view.Notice.Contains("挡下1点") && view.Notice.Contains("实受1点"), "prevented health loss is capped by the health the hero really had");

            game = Controller(); enemy = Enemy(game); game.Battle.Progress.Health = 1; game.TryBlock();
            Call(game, "AttackHero", enemy, true, 2.6); view = game.ReadDesktopPlayerView();
            Check(game.Battle.Progress.Dead && !view.RecentEvents.Any(e => e.Contains("格挡成功")) && view.RecentEvents.Any(e => e.Contains("受到1点伤害")), "a lethal guarded hit never invents a successful guard or zero damage");

            game = Controller(); enemy = Enemy(game); game.Battle.CultivationDefenseFactor = 0; game.TryBlock();
            int eventsBefore = game.ReadDesktopPlayerView().RecentEvents.Length;
            Call(game, "AttackHero", enemy, true, 2.6); view = game.ReadDesktopPlayerView();
            Check(game.Battle.Progress.Health == 100 && view.RecentEvents.Length == eventsBefore && !game.Battle.LastReward.Contains("格挡成功"), "no lost health and no prevented loss produce no fabricated hit result");
        }

        static void WarningChecks()
        {
            AdventureController game = Controller(); AdventureEnemy enemy = Enemy(game);
            enemy.AttackTime = 1.99;
            Check(!game.ReadDesktopPlayerView().BlockText.Contains("即将出手"), "an attack farther than the 0.6-second warning window stays quiet");
            enemy.AttackTime = 2.0;
            DesktopPlayerView view = game.ReadDesktopPlayerView();
            Check(view.BlockText.Contains("木芽小妖即将出手") && view.BlockText.Contains("0.6秒"), "the in-range enemy announces its attack at the 0.6-second boundary");
            string hud = (string)typeof(AdventureController).GetProperty("DesktopHudHint", Private).GetValue(game, null);
            Check(hud.Contains("即将受击") && hud.Contains("点修士格挡") && hud.Length <= 18, "the imminent warning and guard action fit the existing eighteen-character hero HUD");
            Check(game.Battle.Progress.Health == 100 && enemy.AttackTime == 2.0, "reading an attack warning cannot advance combat or damage the hero");
            enemy.X += 400;
            Check(!game.ReadDesktopPlayerView().BlockText.Contains("即将出手"), "an out-of-range enemy cannot warn using a stale attack timer");
            enemy.X -= 400; enemy.Dragging = true;
            Check(!game.ReadDesktopPlayerView().BlockText.Contains("即将出手"), "a dragged enemy cannot announce an attack that the engine suppresses");
            enemy.Dragging = false; game.Battle.CancelQueue();
            Check(!game.ReadDesktopPlayerView().BlockText.Contains("即将出手"), "an unselected mortal opponent cannot announce a forbidden attack");
            game.Battle.Authorize(enemy); Set(game, "enabled", false);
            Check(!game.ReadDesktopPlayerView().BlockText.Contains("即将出手"), "a disabled game cannot announce an attack that will not advance");
            Set(game, "enabled", true); game.Battle.Progress.Dead = true;
            Check(!game.ReadDesktopPlayerView().BlockText.Contains("即将出手"), "a defeated hero sees recovery instead of an incoming attack warning");
            game.Battle.Progress.Dead = false;
            game.Battle.Authorize(enemy); enemy.AttackTime = 2.0;
            Call(game, "AttackHero", enemy, true, .59);
            Check(game.Battle.Progress.Health == 100 && game.ReadDesktopPlayerView().BlockText.Contains("即将出手"), "the warning does not move the original attack threshold earlier");
            Call(game, "AttackHero", enemy, true, .02);
            Check(game.Battle.Progress.Health == 94 && !game.ReadDesktopPlayerView().BlockText.Contains("即将出手"), "the original due hit clears its warning after the attack timer resets");
            game = Controller(); enemy = Enemy(game); enemy.IsBoss = true; enemy.X = 95; enemy.Y = 20; enemy.AttackTime = 1.3;
            Check(game.ReadDesktopPlayerView().BlockText.Contains("即将出手"), "boss warnings use their original 1.8-second cadence");
            enemy.EvilTheme = "CN"; enemy.BossWeapon = "laser"; enemy.AttackTime = 2.2;
            Check(game.ReadDesktopPlayerView().BlockText.Contains("即将出手"), "armed boss warnings include the original weapon cooldown");
        }

        static int Main()
        {
            try { HitChecks(); WarningChecks(); Console.WriteLine("Desktop combat feedback: " + checks + " checks, " + failures + " failures. No forms, probes, saves, actors or natural-time advancement."); return failures == 0 ? 0 : 1; }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
    }
}
