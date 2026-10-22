using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NetBunny
{
    // A detached, read-only-in-effect snapshot for the desktop HUD and panel.
    // It contains no simulation, input, persistence or maintenance authority.
    internal sealed class DesktopPlayerView
    {
        internal string Objective, Hint, TargetName, AttackText, BlockText, Notice;
        internal bool StrikeReady, BlockReady;
        internal int RestSeconds;
        internal string[] RecentEvents;
        internal string EcologyStatus;
        internal DesktopMaintenanceEntry[] MaintenanceResults;
        internal DesktopEnemyChoice[] Enemies;
    }
    internal sealed class DesktopEnemyChoice
    {
        internal string Id, Name, Source, Action;
        internal int Health, MaxHealth;
        internal bool Selected;
    }

    internal sealed partial class AdventureController
    {
        private readonly Queue<string> desktopRecentEvents = new Queue<string>();
        private string desktopPlayerNotice = "";

        private string DesktopHudHint
        {
            get
            {
                if (battle.Progress.Dead)
                {
                    int remaining = Math.Max(0, (int)Math.Ceiling(CultivationBalance.ReviveRestSeconds * CultivationCombatPower.ReviveFactor(cultivation) * DongtianReviveFactor() - battle.DeathTime));
                    return "调息 " + remaining + "秒 · 自动恢复";
                }
                if (cultivation.CanAwaken) return "历练10/10 · 右键觉醒";
                if (battle.Automatic) return "自动作战 · 右键修行";
                AdventureEnemy target = battle.Target;
                if (target == null) return cultivation.Mortal ? "历练" + cultivation.MortalKills + "/10 · 点妖物选敌" : "点妖物历练 · 右键修行";
                if (EnemyHeroDistance(target) > battle.Weapon.Range * displayScale * .8 + .01) return "正在接近 · 点修士格挡";
                if (battle.Cooldown > 0) return "出招冷却 " + battle.Cooldown.ToString("0.0", CultureInfo.InvariantCulture) + "秒";
                return "出招就绪 · 点妖物攻击";
            }
        }

        private void RecordDesktopEvent(string text)
        {
            if (!IsolatedDesktop || String.IsNullOrEmpty(text)) return;
            desktopPlayerNotice = text;
            if (desktopRecentEvents.Count > 0 && desktopRecentEvents.Last() == text) return;
            desktopRecentEvents.Enqueue(text);
            while (desktopRecentEvents.Count > 6) desktopRecentEvents.Dequeue();
        }

        private string DesktopEnemyName(AdventureEnemy enemy)
        {
            if (enemy.ProcessTarget != null) return "程灵 · " + enemy.ProcessTarget.Name;
            if (enemy.NoFieldClear) return enemy.IsBoss ? "蜃楼主镜" : "蜃楼镜妖";
            return (enemy.Elite ? "精英 · " : "") + ArtDemoMonsterLabel(enemy.MonsterKind);
        }

        // Unengaged ordinary fiends remain desktop scenery during mortal play.
        // Mirror encounters, bosses and every non-desktop path keep their rules.
        private bool DesktopMortalUnengaged(AdventureEnemy enemy)
        {
            if(!IsolatedDesktop||!cultivation.Mortal||battle.Target==enemy)return false;
            if(DesktopObservesSystem)return !enemy.NoFieldClear||enemy.ProcessTarget!=null;
            return !enemy.IsBoss&&!enemy.NoFieldClear&&String.IsNullOrEmpty(enemy.EvilTheme);
        }

        internal bool DesktopChooseEnemy(string id, bool strike)
        {
            if (!IsolatedDesktop || disposed || IsDemo || battle.Progress.Dead) return false;
            AdventureEnemy target = battle.Enemies.FirstOrDefault(e => e.Id == id
                && !e.Defeated && !e.VisualDefeated && !e.Cleaning && !e.CleanupStarted && !e.Failed && !e.IsDiskWarning);
            if (target == null) return false;
            bool changed = battle.Target != target;
            if (changed && DesktopObservesSystem)
            {
                if (!battle.SelectDesktopTarget(target)) return false;
            }
            else if (changed)
            {
                // The desktop field is virtual. If an unexpected file-backed or
                // in-flight job exists, refuse rather than touching its queue.
                if (battle.Enemies.Any(e => e.Authorized && (!e.IsVirtual || e.CleanupStarted))) return false;
                bool resumeAutomatic = battle.Automatic;
                battle.CancelQueue();
                battle.Authorize(target);
                if (resumeAutomatic) battle.SetAutomatic(true);
            }
            else battle.Authorize(target);
            if (changed) RecordDesktopEvent("选中" + DesktopEnemyName(target) + " · 优先接近此目标");
            if (strike && cultivation.Mortal) TryMortalStrike(target);
            RenderWorld(true);
            return true;
        }

        internal DesktopPlayerView ReadDesktopPlayerView()
        {
            if (!IsolatedDesktop || disposed) throw new InvalidOperationException("玩家视图仅适用于正在运行的隔离桌面版。");
            AdventureEnemy target = battle.Target;
            double now = clock.Elapsed.TotalSeconds;
            bool dead = battle.Progress.Dead;
            int rest = dead ? Math.Max(0, (int)Math.Ceiling(CultivationBalance.ReviveRestSeconds
                * CultivationCombatPower.ReviveFactor(cultivation) * DongtianReviveFactor() - battle.DeathTime)) : 0;
            string objective;
            if (cultivation.Mortal)
                objective = cultivation.CanAwaken ? "十次历练已满 · 主动觉醒灵根，踏入炼气"
                    : "红尘历练 " + cultivation.MortalKills + "/10 · 逐招击败妖物，十杀后觉醒";
            else if (cultivation.CanAttemptBreakthrough)
                objective = cultivation.RealmName + "圆满 · 破境契机已至，可打开破境仪式";
            else if (cultivation.RealmComplete)
                objective = cultivation.IsFinalRealm ? "已达目前开放的最高境界 · 继续桌面修行"
                    : cultivation.RealmName + "圆满 · 继续积累感悟或寻找破境契机（" + cultivation.BreakthroughInsight + "/" + CultivationBalance.InsightOpportunityThreshold + "）";
            else
                objective = cultivation.RealmName + cultivation.Layer + "层 → " + (cultivation.Layer + 1) + "层 · 修为 "
                    + cultivation.Qi + "/" + cultivation.NextLayerCost + (cultivation.RealmIndex == 0 && cultivation.Layer < 10 ? " · 炼气十层可入千面蜃楼" : "");
            string attack;
            bool inRange = target != null && EnemyHeroDistance(target) <= battle.Weapon.Range * displayScale * .8 + .01;
            bool ready = !dead && !battle.Automatic && target != null && inRange && battle.Cooldown <= 0;
            if (dead) attack = "调息中，恢复后再出招";
            else if (battle.Automatic) attack = "自动作战中 · 修士自主索敌出招";
            else if (target == null) attack = "先选择一只妖物";
            else if (!inRange) attack = "正在接近" + DesktopEnemyName(target) + " · 进入射程后再出招";
            else if (battle.Cooldown > 0) attack = "出招冷却 " + battle.Cooldown.ToString("0.0", CultureInfo.InvariantCulture) + "秒";
            else attack = "出招就绪 · 点击目标打一招";
            bool blockReady = !dead && now >= blockCooldownUntil;
            string block = dead ? "调息中" : now < blockUntil ? "格挡生效中 · 卸去本次来袭大半伤害"
                : blockReady ? "格挡就绪 · 点击修士，持续1秒" : "格挡冷却 " + Math.Max(1, (int)Math.Ceiling(blockCooldownUntil - now)) + "秒";
            string notice = failure ?? (!desktopLastSaveSucceeded ? scanStatus : dead
                ? "力竭调息中 · 约" + rest + "秒后自动恢复，历练与修为保留"
                : cultivation.CanAwaken ? "十次有效击杀已达成 · 打开修行面板，点击灵根觉醒"
                : String.IsNullOrEmpty(desktopPlayerNotice) ? cultivation.Mortal ? "未选中的妖物只会巡游；准备好后再选择目标"
                    : cultivation.DaoTitle + " · " + cultivation.RootName + " · " + cultivation.Technique.Name
                : desktopPlayerNotice);
            // Identity remains visible after awakening even when an ordinary
            // encounter spawns or a combat event updates the short notice.
            if (!dead && !cultivation.Mortal && notice.IndexOf(cultivation.RootName, StringComparison.Ordinal) < 0)
                notice = cultivation.DaoTitle + " · " + cultivation.RootName + "\n" + notice;
            return new DesktopPlayerView
            {
                Objective = objective,
                Hint = dead ? "无需手动复活，调息完成后可继续修行" : cultivation.Mortal
                    ? "点妖物选敌并逐招攻击；点修士格挡；拖动移动；右键打开修行面板"
                    : "自动战斗与吐纳已解锁；功能面板关闭后继续修行，右键可打开操作",
                TargetName = target == null ? "未选择目标" : DesktopEnemyName(target) + " " + target.Health + "/" + target.MaxHealth,
                AttackText = attack, BlockText = block, Notice = notice, StrikeReady = ready, BlockReady = blockReady,
                RestSeconds = rest, RecentEvents = desktopRecentEvents.Reverse().ToArray(),
                EcologyStatus = DesktopEcologyStatus, MaintenanceResults = desktopMaintenance.Reverse().ToArray(),
                Enemies = battle.Enemies.Where(e => !e.Defeated && !e.VisualDefeated && !e.Cleaning && !e.Failed)
                    .Select(e => new DesktopEnemyChoice { Id = e.Id, Name = DesktopEnemyName(e), Health = e.Health, MaxHealth = e.MaxHealth, Selected = e == target,
                        Source = e.ProcessTarget != null ? "所选程序 · PID " + e.ProcessTarget.Id : e.IsVirtual ? "历练妖物 · 虚拟遭遇" : e.IsDiskBoss ? "磁盘空间预警" : e.IsBoss ? "电脑内存压力" : "临时文件 · " + (e.Batch == null ? "来源待复核" : e.Batch.FilesCount + "个 / " + AdventureBattle.FormatBytes(e.Bytes)),
                        Action = e.ProcessTarget != null ? "击败后单独确认正常退出" : e.IsVirtual ? "仅结算修行奖励" : (e.IsBoss && !e.IsDiskBoss ? consent.MemoryEnabled : consent.CleanupEnabled) ? "已授权对应维护范围" : "未授权 · 本次战斗仅结算修行奖励" }).ToArray()
            };
        }
    }
}
