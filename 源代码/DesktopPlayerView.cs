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
        internal string NextStep, NextActionId, NextActionText;
        internal string TargetSourceDetail, TargetAction;
        internal DesktopMaintenanceEntry[] MaintenanceResults;
        internal DesktopEnemyChoice[] Enemies;
        internal DesktopReturnTask[] ReturnTasks;
    }
    internal sealed class DesktopReturnTask
    {
        internal string PanelId, Text, ActionText;
    }
    internal sealed class DesktopEnemyChoice
    {
        internal string Id, Name, Source, SourceDetail, Action;
        internal int Health, MaxHealth;
        internal bool Selected, CanSelect;
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
                string incoming = DesktopIncomingAttackHint();
                if (incoming.Length > 0) return "即将受击" + (clock.Elapsed.TotalSeconds < blockUntil ? " · 格挡生效" : clock.Elapsed.TotalSeconds >= blockCooldownUntil ? " · 点修士格挡" : " · 格挡冷却");
                if (cultivation.CanAwaken) return "历练10/10 · 右键觉醒";
                string result = DesktopCombatResultHint();
                if (result.Length > 0) return result;
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
            if (!enemy.IsVirtual && enemy.IsBoss) return enemy.IsDiskBoss ? enemy.IsDiskWarning ? "磁盘空间预警" : "磁盘危机" : "内存分身";
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
            if (target == null) { RecordDesktopEvent("目标已变化或正在处理，请重新选择可交互的妖物"); return false; }
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
            // The shared strike method already enforces manual mode, range,
            // cooldown and recovery. Cultivators can choose manual combat too.
            if (strike) TryMortalStrike(target);
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
            string incomingAttack = DesktopIncomingAttackHint();
            if (incomingAttack.Length > 0) block = incomingAttack + "\n" + block;
            string notice = failure ?? DesktopConsentFailureNotice ?? (!desktopLastSaveSucceeded ? scanStatus : dead
                ? "力竭调息中 · 约" + rest + "秒后自动恢复，历练与修为保留"
                : cultivation.CanAwaken ? "十次有效击杀已达成 · 打开修行面板，点击灵根觉醒"
                : String.IsNullOrEmpty(desktopPlayerNotice) ? cultivation.Mortal ? "未选中的妖物只会巡游；准备好后再选择目标"
                    : cultivation.DaoTitle + " · " + cultivation.RootName + " · " + cultivation.Technique.Name
                : desktopPlayerNotice);
            // Identity remains visible after awakening even when an ordinary
            // encounter spawns or a combat event updates the short notice.
            if (!dead && !cultivation.Mortal && notice.IndexOf(cultivation.RootName, StringComparison.Ordinal) < 0)
                notice = cultivation.DaoTitle + " · " + cultivation.RootName + "\n" + notice;
            DesktopEnemyChoice[] enemies = battle.Enemies.Where(e => !e.Defeated && !e.VisualDefeated && !e.Cleaning && !e.Failed)
                .Select(DescribeDesktopEnemy).ToArray();
            DesktopEnemyChoice selected = enemies.FirstOrDefault(e => e.Selected);
            string nextStep, actionId, actionText;
            DesktopNextStep(dead, rest, out nextStep, out actionId, out actionText);
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
                Enemies = enemies, NextStep = nextStep, NextActionId = actionId, NextActionText = actionText,
                ReturnTasks = ReadDesktopReturnTasks(DateTime.UtcNow),
                TargetSourceDetail = selected == null ? "尚未选中妖物。先从桌面或手札选敌，再查看该目标的来源。" : selected.SourceDetail,
                TargetAction = selected == null ? "选择目标不会授予任何电脑维护权限。" : selected.Action
            };
        }

        private DesktopEnemyChoice DescribeDesktopEnemy(AdventureEnemy enemy)
        {
            string source, detail;
            if (enemy.ProcessTarget != null)
            {
                ProcessIdentity p = enemy.ProcessTarget;
                source = "所选程序 · PID " + p.Id;
                detail = "程序：" + p.Name + " · PID " + p.Id + "\n路径：" + p.ExecutablePath
                    + "\n启动标识：" + p.StartedUtcTicks + "\n执行前会重新核对身份，并单独询问本次正常退出。";
            }
            else if (enemy.NoFieldClear)
            { source = "千面蜃楼 · 秘境遭遇"; detail = "来自正在进行的灵域历练，与电脑文件或进程无关；按正式战斗结算秘境进度。"; }
            else if (enemy.IsVirtual)
            { source = "历练妖物 · 虚拟遭遇"; detail = "桌面修行生成的历练妖物，没有绑定文件或程序。电脑干净时也能继续修行。"; }
            else if (enemy.IsDiskWarning)
            { source = "磁盘空间预警"; detail = "当前空间不足，但未找到可安全处理的临时文件。此项只是预警，请自行管理磁盘空间。"; }
            else if (enemy.IsBoss && !enemy.IsDiskBoss)
            { source = "电脑内存压力"; detail = "来源：系统可用内存采样。维护仅整理工作集，不结束程序，实际结果单独记录。"; }
            else
            {
                source = "临时文件 · " + (enemy.Batch == null ? "来源待复核" : enemy.Batch.FilesCount + "个 / " + AdventureBattle.FormatBytes(enemy.Bytes));
                detail = enemy.Batch == null ? "尚无可核实的文件批次；不会据此执行清理。"
                    : enemy.Batch.Label + " · " + enemy.Batch.FilesCount + "个 / " + AdventureBattle.FormatBytes(enemy.Batch.Bytes)
                        + "\n绑定文件（最多显示5项）：\n" + String.Join("\n", enemy.Batch.Candidates.Take(5).Select(f => f.Path).ToArray())
                        + (enemy.Batch.FilesCount > 5 ? "\n另有 " + (enemy.Batch.FilesCount - 5) + " 项" : "")
                        + "\n仅依据本批次快照；实际处理前复核位置、文件身份、时龄与占用。";
            }
            string action;
            if (enemy.IsDiskWarning) action = "仅为空间预警 · 无可安全清理文件，请自行管理空间";
            else if (!DesktopObservesSystem) action = "虚拟历练 · 系统维护关闭";
            else if (enemy.ProcessTarget != null) action = "击败后单独确认本次正常退出 · 不会自动关闭";
            else if (enemy.IsVirtual || enemy.NoFieldClear) action = "仅结算修行奖励";
            else
            {
                bool memory = enemy.IsBoss && !enemy.IsDiskBoss;
                bool granted = memory ? consent.MemoryEnabled : consent.CleanupEnabled;
                bool automatic = enemy.Authorization == AdventureAuthorization.Automatic || (enemy.Authorization == AdventureAuthorization.None && battle.Automatic);
                if (!granted) action = "未授权 · 本次战斗仅结算修行奖励";
                else if (automatic && !consent.AutoMaintenanceEnabled) action = "自动维护未授权 · 自动战斗仅结算修行奖励";
                else action = (memory ? "击败后尝试整理工作集" : "击败后尝试清理绑定文件") + " · 实际结果另行核实";
            }
            return new DesktopEnemyChoice { Id = enemy.Id, Name = DesktopEnemyName(enemy), Source = source, SourceDetail = detail, Action = action,
                Health = enemy.Health, MaxHealth = enemy.MaxHealth, Selected = enemy == battle.Target,
                CanSelect = !battle.Progress.Dead && !enemy.IsDiskWarning && !enemy.CleanupStarted };
        }

        private DesktopReturnTask[] ReadDesktopReturnTasks(DateTime utc)
        {
            var tasks = new List<DesktopReturnTask>();
            JiyuanDef offer = JiyuanEngine.FindPendingOffer(lingyu);
            if (lingyu.RealmEntered.Contains("qianmian") && !String.IsNullOrEmpty(lingyu.ExpeditionNode)
                && (!lingyu.ExpeditionActive || lingyu.CombatPending || offer != null))
            {
                string text = "千面蜃楼 · " + MirageEngine.NodeTitle(lingyu.ExpeditionNode);
                if (!lingyu.ExpeditionActive) text += " · 已暂存";
                if (lingyu.CombatPending) text += " · 镜妖待战";
                else if (offer != null) text += " · 待抉择：" + offer.Name;
                tasks.Add(new DesktopReturnTask { PanelId = "lingyu", Text = text, ActionText = "查看灵域" });
            }
            if (DongtianUnlocked)
            {
                int fields = Enumerable.Range(0, dongtian.UnlockedFields).Count(i => dongtian.FieldReady(i, utc));
                int pills = dongtian.FurnaceReady(utc) ? 1 : 0, gears = dongtian.ForgeReady(utc) ? 1 : 0;
                if (fields + pills + gears > 0)
                    tasks.Add(new DesktopReturnTask { PanelId = "dongtian", Text = "洞天可收取 · 灵田" + fields + "畦 / 丹炉" + pills + "份 / 器位" + gears + "件", ActionText = "查看洞天" });
            }
            return tasks.ToArray();
        }

        private void DesktopNextStep(bool dead, int rest, out string step, out string actionId, out string actionText)
        {
            actionId = actionText = "";
            if (dead) { step = "先调息恢复，约" + rest + "秒。进度保留，无需重新开局。"; return; }
            if (cultivation.Mortal)
            {
                if (cultivation.CanAwaken) { step = "十次有效击杀已满，主动觉醒后解锁自动战斗与吐纳。"; actionId = "awaken"; actionText = "引气入体 · 觉醒"; }
                else step = "选中妖物后，接近并逐招攻击；来袭时点修士格挡。十次有效击杀后觉醒。";
                return;
            }
            if (cultivation.CanAttemptBreakthrough || cultivation.RealmComplete && !cultivation.IsFinalRealm)
            { step = cultivation.CanAttemptBreakthrough ? "本境契机已就绪。查看正式概率与可选材料，由你发起破境。" : "本境已圆满。寻找蜃楼、百业或苦修契机；继续修炼可积累感悟。"; actionId = "breakthrough"; actionText = "查看破境与契机"; return; }
            if (lingyu.ExpeditionActive)
            { step = "灵域历练正在进行。查看当前事件、战斗或待领取结果。"; actionId = "lingyu"; actionText = "继续灵域历练"; return; }
            if (DongtianUnlocked)
            {
                DateTime utc = DateTime.UtcNow;
                bool ready = Enumerable.Range(0, dongtian.UnlockedFields).Any(i => dongtian.FieldReady(i, utc)) || dongtian.FurnaceReady(utc) || dongtian.ForgeReady(utc);
                bool empty = Enumerable.Range(0, dongtian.UnlockedFields).Any(i => String.IsNullOrEmpty(dongtian.Fields[i]));
                step = ready ? "洞天已有成熟作物或炼制成果，收取后可用于丹药、器具与扩建。"
                    : empty ? (dongtian.Fields.Take(dongtian.UnlockedFields).Any(f => !String.IsNullOrEmpty(f)) ? "已有作物在自然生长，成熟后可收获；空田可继续播种，积攒炼丹与扩建材料。" : "洞天已有空田。先种灵谷积攒材料，再炼丹、铸器或扩建。")
                    : "灵田正在自然生长，成熟后再收获。现在可以收起面板，继续桌面修行。";
                actionId = "dongtian"; actionText = ready ? "前往洞天收获" : empty ? "前往洞天播种" : "查看洞天进度"; return;
            }
            if (cultivation.RealmIndex == 0 && cultivation.Layer >= CultivationBalance.LingsuiMinLayer)
            { step = "已可进入千面蜃楼。探索机缘、争取水月灵髓，为筑基与洞天积攒家底。"; actionId = "lingyu"; actionText = "前往千面蜃楼"; return; }
            step = "吐纳与战斗持续积累修为。炼气十层可入千面蜃楼；日常工作可自选开启百业。";
            actionId = "training"; actionText = "查看修行与功法";
        }
    }
}
