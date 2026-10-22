using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
        static string Hud(AdventureController game) { return (string)typeof(AdventureController).GetProperty("DesktopHudHint", Private).GetValue(game, null); }
        static bool StrikeResult(AdventureController game, AdventureEnemy enemy, int before)
        {
            MethodInfo method = typeof(AdventureController).GetMethod("RecordDesktopStrikeResult", Private);
            if (method == null) return false;
            method.Invoke(game, new object[] { enemy, before }); return true;
        }
        static void ExpireFeedback(AdventureController game)
        { FieldInfo field = typeof(AdventureController).GetField("feedbackExpiry", Private); if (field != null) field.SetValue(game, -1.0); }

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

        static void ShortHudChecks()
        {
            AdventureController game = Controller(); AdventureEnemy enemy = Enemy(game);
            string operation = Hud(game);
            Call(game, "AttackHero", enemy, true, 2.6);
            Check(Hud(game).Contains("受伤6") && new StringInfo(Hud(game)).LengthInTextElements <= 18,
                "actual AttackHero loss appears briefly in the original HUD within eighteen text elements");
            FieldInfo expiryField = typeof(AdventureController).GetField("feedbackExpiry", Private);
            double expiry = expiryField == null ? -1 : (double)expiryField.GetValue(game);
            double wallNow = ((Stopwatch)typeof(AdventureController).GetField("clock", Private).GetValue(game)).Elapsed.TotalSeconds;
            Check(expiry > wallNow && expiry <= wallNow + 2, "the short result has a bounded actual Stopwatch expiry rather than game-tick duration");
            game.Battle.SetAutomatic(true);
            Check(Hud(game).Contains("受伤6"), "automatic operation text cannot hide a fresh actual injury");
            enemy.Health -= 7; StrikeResult(game, enemy, enemy.Health + 7);
            Check(Hud(game).Contains("受伤6") && !Hud(game).Contains("命中"), "a following automatic strike cannot overwrite a still-visible injury");
            Check(expiryField != null && (double)expiryField.GetValue(game) == expiry, "suppressed hits cannot keep an injury notification alive forever by extending its expiry");
            enemy.AttackTime = 2.0;
            Check(Hud(game).Contains("即将受击") && !Hud(game).Contains("受伤6"), "imminent incoming warning outranks a previous combat result");
            enemy.AttackTime = 0; game.Cultivation.MortalKills = 10;
            Check(Hud(game).Contains("觉醒"), "available awakening outranks a previous combat result");
            game.Battle.Progress.Dead = true;
            Check(Hud(game).Contains("调息") && !Hud(game).Contains("受伤6"), "death recovery remains above awakening and the short result");
            game.Battle.Progress.Dead = false; game.Cultivation.MortalKills = 0; game.Battle.SetAutomatic(false); ExpireFeedback(game);
            Check(Hud(game) == operation, "expired result restores the original operation hint without changing the game clock");

            game = Controller(); enemy = Enemy(game); game.TryBlock(); Call(game, "AttackHero", enemy, true, 2.6);
            string blocked = Hud(game);
            Check(blocked.Contains("挡下5") && blocked.Contains("受伤1") && new StringInfo(blocked).LengthInTextElements <= 18,
                "actual guard result shows five prevented and one lost HP in the same compact HUD");
            game.Cultivation.Mortal = false; game.Battle.SetAutomatic(true); int history = game.ReadDesktopPlayerView().RecentEvents.Length;
            enemy.Health -= 3; bool methodExists = StrikeResult(game, enemy, enemy.Health + 3);
            Check(methodExists, "post-strike recorder exists as the private partial-controller integration seam");
            Check(Hud(game) == blocked, "subsequent automatic hits do not drown out successful guard feedback");
            Check(game.ReadDesktopPlayerView().RecentEvents.Length == history, "automatic strike results do not fill the six-entry manual history");
            ExpireFeedback(game); enemy.Health -= 4; StrikeResult(game, enemy, enemy.Health + 4);
            Check(Hud(game).Contains("命中4"), "an actual strike becomes visible once the protected guard result expires");
            enemy.Health -= 2; StrikeResult(game, enemy, enemy.Health + 2);
            Check(Hud(game).Contains("命中2"), "ordinary hit feedback can refresh to the newest real amount");
            string beforeZero = Hud(game); StrikeResult(game, enemy, enemy.Health); StrikeResult(game, null, 10);
            Check(Hud(game) == beforeZero, "zero-damage and missing targets do not manufacture a new hit result");
            int hp = game.Battle.Progress.Health, enemyHp = enemy.Health, qi = (int)game.Cultivation.Qi; double attack = enemy.AttackTime;
            int events = game.ReadDesktopPlayerView().RecentEvents.Length;
            for (int i = 0; i < 20; i++) Hud(game);
            Check(game.Battle.Progress.Health == hp && enemy.Health == enemyHp && game.Cultivation.Qi == qi && enemy.AttackTime == attack
                && game.ReadDesktopPlayerView().RecentEvents.Length == events, "reading the short result has no damage, growth, timer or history side effect");
            Set(game, "enabled", false);
            Check(!Hud(game).Contains("命中2"), "disabled desktop does not display a remembered combat result");
            Set(game, "enabled", true); Set(game, "isolatedProfileRoot", null);
            Check(!Hud(game).Contains("命中2"), "legacy nonisolated mode does not display the new desktop-only result");

            game = Controller(); enemy = Enemy(game); enemy.Health = 137; StrikeResult(game, enemy, 144);
            Check(game.ReadDesktopPlayerView().RecentEvents.Any(e => e == "出招命中木芽小妖 · 伤害 7 · 剩余气血 137/144"),
                "mortal strike recorder preserves the original full manual hit history text");
            ExpireFeedback(game); StrikeResult(game, enemy, enemy.Health);
            Check(!Hud(game).Contains("命中"), "a zero-loss attempted strike cannot revive an expired hit notification");
            game = Controller(); enemy = Enemy(game); game.Battle.CultivationDefenseFactor = 0; Call(game, "AttackHero", enemy, true, 2.6);
            Check(!Hud(game).Contains("受伤") && !Hud(game).Contains("挡下"), "real zero-loss incoming path creates no short injury or guard notification");
            game = Controller(); enemy = Enemy(game); int before = enemy.Health;
            game.Battle.Strike(enemy); StrikeResult(game, enemy, before);
            Check(before > enemy.Health && Hud(game).Contains("命中" + (before - enemy.Health).ToString(CultureInfo.InvariantCulture)),
                "the real shared Battle.Strike HP delta, rather than a nominal weapon value, feeds the compact hit result");
        }

        static int Main()
        {
            try { HitChecks(); WarningChecks(); ShortHudChecks(); Console.WriteLine("Desktop combat feedback: " + checks + " checks, " + failures + " failures. No forms, probes, saves, actors or natural-time advancement."); return failures == 0 ? 0 : 1; }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
    }
}
