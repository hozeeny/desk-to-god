using System;
using System.Globalization;

namespace NetBunny
{
    internal sealed partial class AdventureController
    {
        private string feedbackText = "";
        private double feedbackExpiry = -1;
        private bool feedbackImportant;

        private string DesktopCombatResultHint()
        {
            return !IsolatedDesktop || !enabled || disposed || IsDemo || clock.Elapsed.TotalSeconds >= feedbackExpiry
                ? "" : feedbackText ?? "";
        }

        private void SetDesktopCombatFeedback(string text, bool important)
        {
            if (!IsolatedDesktop || !enabled || disposed || IsDemo) return;
            double now = clock.Elapsed.TotalSeconds;
            if (!important && feedbackImportant && now < feedbackExpiry) return;
            // Never cut a number in half to fit the original head strip. The
            // rare oversized result remains available in the full manual log.
            feedbackText = new StringInfo(text).LengthInTextElements <= 18 ? text : "战果已记录·查看手札";
            feedbackImportant = important;
            feedbackExpiry = now + 1.8;
        }

        private void RecordDesktopStrikeResult(AdventureEnemy target, int healthBefore)
        {
            if (!IsolatedDesktop || target == null || healthBefore <= target.Health) return;
            int lost = Math.Max(0, healthBefore - Math.Max(0, target.Health));
            if (lost == 0) return;
            SetDesktopCombatFeedback("命中" + lost.ToString(CultureInfo.InvariantCulture), false);
            if (cultivation.Mortal)
                RecordDesktopEvent("出招命中" + DesktopEnemyName(target) + " · 伤害 " + (healthBefore - target.Health) + " · 剩余气血 " + target.Health + "/" + target.MaxHealth);
        }

        // Both the real attack and its read-only warning use these original
        // cadence/reach rules; displaying a warning never advances AttackTime.
        private static double EnemyAttackCooldown(AdventureEnemy enemy)
        { return !String.IsNullOrEmpty(enemy.EvilTheme) ? 2.4 + AdventureWeapon.Find(enemy.BossWeapon).Cooldown : enemy.IsBoss ? 1.8 : AdventureMonster.Find(enemy.MonsterKind).AttackCooldown; }

        private static double EnemyAttackReach(AdventureEnemy enemy)
        { return !String.IsNullOrEmpty(enemy.EvilTheme) ? Math.Min(300, AdventureWeapon.Find(enemy.BossWeapon).Range * .65) : enemy.IsBoss ? 105 : 86; }

        private string DesktopIncomingAttackHint()
        {
            if (!IsolatedDesktop || !enabled || IsDemo || battle.Progress.Dead
                || networkState.Phase == KnightPhase.Dying || networkState.Phase == KnightPhase.Grave || networkState.Phase == KnightPhase.Revive) return "";
            AdventureEnemy next = null;
            double soonest = Double.MaxValue;
            foreach (AdventureEnemy enemy in battle.Enemies)
            {
                if (DesktopMortalUnengaged(enemy) || enemy.Dragging || enemy.Cleaning || enemy.VisualDefeated || enemy.Defeated || enemy.Failed || enemy.IsDiskWarning
                    || EnemyHeroDistance(enemy) > EnemyAttackReach(enemy) * displayScale) continue;
                double remaining = EnemyAttackCooldown(enemy) - enemy.AttackTime;
                if (remaining < 0 || remaining > .6 + .000000001 || remaining >= soonest) continue;
                next = enemy; soonest = remaining;
            }
            return next == null ? "" : DesktopEnemyName(next) + "即将出手 · 约" + Math.Max(.1, soonest).ToString("0.0", CultureInfo.InvariantCulture) + "秒";
        }

        private void RecordDesktopIncomingHit(AdventureEnemy enemy, int healthBefore, int unblockedIncoming, bool blocked)
        {
            if (!IsolatedDesktop) return;
            int lost = Math.Max(0, healthBefore - battle.Progress.Health);
            int unblockedLoss = Math.Min(healthBefore, (int)Math.Round(Math.Max(0, unblockedIncoming) * battle.CultivationDefenseFactor));
            int prevented = blocked ? Math.Max(0, unblockedLoss - lost) : 0;
            if (lost == 0 && prevented == 0) return;
            SetDesktopCombatFeedback(prevented > 0 ? "挡下" + prevented.ToString(CultureInfo.InvariantCulture) + "·受伤" + lost.ToString(CultureInfo.InvariantCulture)
                : "受伤" + lost.ToString(CultureInfo.InvariantCulture), true);
            string result = prevented > 0
                ? "格挡成功 · " + DesktopEnemyName(enemy) + " · 挡下" + prevented + "点 · 实受" + lost + "点伤害"
                : (blocked ? "格挡未能卸伤 · " : "") + DesktopEnemyName(enemy) + "来袭 · 受到" + lost + "点伤害";
            result += " · 气血 " + battle.Progress.Health + "/" + battle.Progress.MaxHealth;
            if (!battle.Progress.Dead) battle.LastReward = result;
            RecordDesktopEvent(result);
        }
    }
}
