using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NetBunny
{
    // 软件灵域与机缘系统（P3）：软件类别映射到修仙灵域，灵域探索与机缘事件
    // 都围绕百业道途展开。本模块是纯逻辑：不依赖 WinForms，存档独立为
    // lingyu-v1.txt。识别规则与活动类别定义全部复用 BaiyeCatalog，
    // 不另维护任何可能冲突的映射。
    //
    // 红线：进入秘境永远不触发真实文件删除、进程关闭或内存回收；
    // 战斗遭遇只生成虚拟妖物（沿用既有虚拟战斗与自动作战）。

    // ---- 灵域定义与进入条件（配置化数据） ----
    internal sealed class LingyuRealmDef
    {
        internal string Id, Name, Description, EntryHint;
        internal string[] LinkedBaiyePaths = new string[0];
        // 进入条件（满足任一即可）：关联道途达到等级，或修仙境界达到门槛。
        // 游戏条件途径保证浏览器/建模等软件未运行时也能进入，玩法不被软件锁死。
        internal int UnlockBaiyeLevel = 1;
        internal int UnlockRealmIndex, UnlockLayer = 10;
        internal bool Playable; // 本阶段只有千面蜃楼完整可玩
    }

    internal static class LingyuCatalog
    {
        internal static readonly LingyuRealmDef[] Realms = {
            new LingyuRealmDef
            {
                Id = "qianmian", Name = "千面蜃楼", Playable = true,
                Description = "浏览器与网上游历化出的幻术灵域，镜像妖物出没",
                LinkedBaiyePaths = new[] { "youli" },
                UnlockBaiyeLevel = 1, UnlockRealmIndex = 0, UnlockLayer = 10,
                EntryHint = "游历道初窥门径，或炼气第 10 层"
            },
            new LingyuRealmDef
            {
                Id = "tiangong", Name = "天工机械城",
                Description = "建模与工程造物化出的机关灵域，器修机缘藏于其中（后续版本开放）",
                LinkedBaiyePaths = new[] { "qi" },
                UnlockBaiyeLevel = 1, UnlockRealmIndex = 1, UnlockLayer = 1,
                EntryHint = "器道初窥门径，或踏入筑基"
            },
            new LingyuRealmDef
            {
                Id = "wenqu", Name = "文曲书院",
                Description = "办公文档与文字创作化出的书卷灵域，书灵墨妖与符箓传承（后续版本开放）",
                LinkedBaiyePaths = new[] { "wen", "huajing" },
                UnlockBaiyeLevel = 1, UnlockRealmIndex = 0, UnlockLayer = 15,
                EntryHint = "文道或画境道初窥门径，或炼气第 15 层"
            },
            new LingyuRealmDef
            {
                Id = "wanxiang", Name = "万象天机楼",
                Description = "AI 协力化出的推演灵域，算灵与天机推演之所（后续版本开放）",
                LinkedBaiyePaths = new[] { "tianji" },
                UnlockBaiyeLevel = 1, UnlockRealmIndex = 1, UnlockLayer = 5,
                EntryHint = "天机道初窥门径，或筑基第 5 层"
            }
        };
        internal static LingyuRealmDef FindRealm(string id)
        {
            for (int i = 0; i < Realms.Length; i++) if (Realms[i].Id == id) return Realms[i];
            return null;
        }
        // 满足任一条件即可进入：道途达标，或境界达标（游戏内途径）。
        internal static bool CanEnter(LingyuRealmDef realm, BaiyeProgress baiye, CultivationProgress cultivation)
        {
            if (realm == null || baiye == null || cultivation == null) return false;
            for (int i = 0; i < realm.LinkedBaiyePaths.Length; i++)
                if (baiye.LevelOf(realm.LinkedBaiyePaths[i]) >= realm.UnlockBaiyeLevel) return true;
            return cultivation.RealmIndex > realm.UnlockRealmIndex ||
                (cultivation.RealmIndex == realm.UnlockRealmIndex && cultivation.Layer >= realm.UnlockLayer);
        }
        // 材料与传承名册（UI 展示名）。
        internal static string MaterialName(string id)
        {
            switch (id)
            {
                case "jinghua-shuiyue": return "镜花水月";
                case "suijing-jing": return "碎镜晶";
                case "jinghua": return "镜花";
                default: return id;
            }
        }
        internal static string ScrollName(string id)
        {
            switch (id)
            {
                case "jingxin-scroll": return "镜心残卷";
                default: return id;
            }
        }
        internal static string HeritageName(string id)
        {
            switch (id)
            {
                case "shuiyue-mirror": return "水月镜符";
                default: return id;
            }
        }
    }

    // 统一奖励描述：修为跨存档结算，材料/残卷/传承落在灵域存档。
    internal sealed class LingyuReward
    {
        internal long Qi;
        internal string MaterialId; internal int MaterialCount;
        internal string ScrollId; internal int ScrollCount = 1;
        internal string HeritageId, HeritageName;
        internal string Text = "";
    }

    // 灵域存档：lingyu-v1.txt，版本号 + 原子写入 + .bak 回退，与其他存档互不干扰。
    internal sealed class LingyuProgress
    {
        internal const int FormatVersion = 1;
        internal readonly Dictionary<string, int> RealmVisits = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly HashSet<string> RealmEntered = new HashSet<string>(StringComparer.Ordinal);
        // 探索支持中断、保存与重访：以下字段完整描述一次进行中的蜃楼之行。
        internal bool ExpeditionActive;
        internal string ExpeditionNode = "";
        internal int ExpeditionDepth;
        internal bool CombatPending;
        // 进行中战斗的结算语义（持久化）：重启后重访，战斗胜利能正确结算或补发，
        // 不会因进程中断丢失主镜通关/机缘战斗的奖励语义。
        internal string CombatOutcome = "";
        internal readonly HashSet<string> ExpeditionFlags = new HashSet<string>(StringComparer.Ordinal);
        internal readonly List<string> ExpeditionLog = new List<string>();
        // 收藏体系：基础材料、功法残卷、传承记录。
        internal readonly Dictionary<string, int> Materials = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly Dictionary<string, int> Scrolls = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly List<string> Heritages = new List<string>();
        // 机缘档案：choices 记录玩家选择；Grants 为奖励台账；PendingQi 等待修仙侧对账。
        internal readonly Dictionary<string, int> JiyuanChoices = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly Dictionary<string, long> Grants = new Dictionary<string, long>(StringComparer.Ordinal);
        internal readonly Dictionary<string, long> PendingQi = new Dictionary<string, long>(StringComparer.Ordinal);
        internal long EventCounter; // 事件实例序号，保证每次奖励的台账 id 唯一且可回放

        internal int Visits(string realmId) { int v; return RealmVisits.TryGetValue(realmId, out v) ? v : 0; }
        internal void LogLine(string text)
        {
            ExpeditionLog.Add(text);
            while (ExpeditionLog.Count > 12) ExpeditionLog.RemoveAt(0);
        }

        internal static LingyuProgress Load(string path)
        {
            LingyuProgress value;
            if (TryLoad(path, out value)) return value;
            if (TryLoad(path + ".bak", out value)) return value;
            return new LingyuProgress();
        }
        private static bool TryLoad(string path, out LingyuProgress progress)
        {
            progress = null;
            if (!File.Exists(path)) return false;
            try
            {
                LingyuProgress next = new LingyuProgress();
                int version = -1;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int equals = line.IndexOf('=');
                    if (equals < 1) continue;
                    string key = line.Substring(0, equals), value = line.Substring(equals + 1);
                    int n; long l;
                    if (key == "version" && Int32.TryParse(value, out n)) version = n;
                    else if (key == "expedition") next.ExpeditionActive = value == "1";
                    else if (key == "node") next.ExpeditionNode = ValidToken(value, 24) ? value : "";
                    else if (key == "depth" && Int32.TryParse(value, out n) && n >= 0 && n < 1000) next.ExpeditionDepth = n;
                    else if (key == "combatpending") next.CombatPending = value == "1";
                    else if (key == "outcome" && ValidToken(value, 48)) next.CombatOutcome = value;
                    else if (key == "counter" && Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out l) && l >= 0) next.EventCounter = l;
                    else if (key.StartsWith("visit.", StringComparison.Ordinal) && Int32.TryParse(value, out n) && n >= 0)
                    { string id = key.Substring(6); if (LingyuCatalog.FindRealm(id) != null) next.RealmVisits[id] = n; }
                    else if (key == "entered" && LingyuCatalog.FindRealm(value) != null) next.RealmEntered.Add(value);
                    else if (key == "flag" && ValidToken(value, 40)) next.ExpeditionFlags.Add(value);
                    else if (key == "explog" && value.Length <= 60 && value.IndexOfAny(new[] { '\r', '\n', '=', '|' }) < 0) next.ExpeditionLog.Add(value);
                    else if (key.StartsWith("mat.", StringComparison.Ordinal) && Int32.TryParse(value, out n) && n > 0)
                    { string id = key.Substring(4); if (ValidToken(id, 40)) next.Materials[id] = n; }
                    else if (key.StartsWith("scroll.", StringComparison.Ordinal) && Int32.TryParse(value, out n) && n > 0)
                    { string id = key.Substring(7); if (ValidToken(id, 40)) next.Scrolls[id] = n; }
                    else if (key == "heritage" && ValidToken(value, 40) && !next.Heritages.Contains(value)) next.Heritages.Add(value);
                    else if (key.StartsWith("jiyuan.", StringComparison.Ordinal) && Int32.TryParse(value, out n) && n >= 0 && n < 10)
                    { string id = key.Substring(7); if (ValidToken(id, 40)) next.JiyuanChoices[id] = n; }
                    else if (key.StartsWith("grant.", StringComparison.Ordinal) && Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out l) && l >= 0)
                    { string id = key.Substring(6); if (ValidToken(id, 48)) next.Grants[id] = l; }
                    else if (key.StartsWith("pendingqi.", StringComparison.Ordinal) && Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out l) && l > 0 && l < 1000000)
                    { string id = key.Substring(10); if (ValidToken(id, 48)) next.PendingQi[id] = l; }
                }
                if (version != FormatVersion) return false;
                // 暂离（expedition=0）只解除战斗挂起，节点与深度保留——重访时从暂离处继续。
                if (!next.ExpeditionActive) { next.CombatPending = false; next.CombatOutcome = ""; }
                if (!next.CombatPending) next.CombatOutcome = "";
                while (next.ExpeditionLog.Count > 12) next.ExpeditionLog.RemoveAt(0);
                progress = next; return true;
            }
            catch { return false; }
        }
        private static bool ValidToken(string value, int max)
        {
            return value.Length > 0 && value.Length <= max && value.IndexOfAny(new[] { '\r', '\n', '=', '|' }) < 0;
        }
        internal void Save(string path)
        {
            string folder = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            string tmp = path + ".new";
            StringBuilder data = new StringBuilder();
            data.Append("version=").Append(FormatVersion).Append('\n');
            data.Append("expedition=").Append(ExpeditionActive ? "1" : "0").Append('\n');
            data.Append("node=").Append(ExpeditionNode).Append('\n');
            data.Append("depth=").Append(ExpeditionDepth).Append('\n');
            data.Append("combatpending=").Append(CombatPending ? "1" : "0").Append('\n');
            data.Append("outcome=").Append(CombatOutcome).Append('\n');
            data.Append("counter=").Append(EventCounter.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (KeyValuePair<string, int> pair in RealmVisits) data.Append("visit.").Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
            foreach (string realm in RealmEntered) data.Append("entered=").Append(realm).Append('\n');
            foreach (string flag in ExpeditionFlags) data.Append("flag=").Append(flag).Append('\n');
            foreach (string entry in ExpeditionLog) data.Append("explog=").Append(entry).Append('\n');
            foreach (KeyValuePair<string, int> pair in Materials) data.Append("mat.").Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
            foreach (KeyValuePair<string, int> pair in Scrolls) data.Append("scroll.").Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
            foreach (string heritage in Heritages) data.Append("heritage=").Append(heritage).Append('\n');
            foreach (KeyValuePair<string, int> pair in JiyuanChoices) data.Append("jiyuan.").Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
            foreach (KeyValuePair<string, long> pair in Grants) data.Append("grant.").Append(pair.Key).Append('=').Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (KeyValuePair<string, long> pair in PendingQi) data.Append("pendingqi.").Append(pair.Key).Append('=').Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            File.WriteAllText(tmp, data.ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
            else File.Move(tmp, path);
        }
    }

    // 统一奖励结算接口：材料/残卷/传承与台账立即落在灵域存档；
    // 修为进入 PendingQi，由控制器按 A1 同款跨存档协议对账（不重发、不丢失）。
    internal static class LingyuSettlement
    {
        // 返回 true 表示本次新发放；同一 grantId 重复调用直接返回 false（幂等）。
        internal static bool TryGrant(LingyuProgress progress, string grantId, LingyuReward reward, DateTime utc)
        {
            if (progress == null || reward == null || String.IsNullOrEmpty(grantId)) return false;
            if (progress.Grants.ContainsKey(grantId)) return false;
            progress.Grants[grantId] = utc.Ticks;
            if (!String.IsNullOrEmpty(reward.MaterialId) && reward.MaterialCount > 0)
                progress.Materials[reward.MaterialId] = (progress.Materials.ContainsKey(reward.MaterialId) ? progress.Materials[reward.MaterialId] : 0) + reward.MaterialCount;
            if (!String.IsNullOrEmpty(reward.ScrollId) && reward.ScrollCount > 0)
                progress.Scrolls[reward.ScrollId] = (progress.Scrolls.ContainsKey(reward.ScrollId) ? progress.Scrolls[reward.ScrollId] : 0) + reward.ScrollCount;
            if (!String.IsNullOrEmpty(reward.HeritageId) && !progress.Heritages.Contains(reward.HeritageId))
                progress.Heritages.Add(reward.HeritageId);
            if (reward.Qi > 0) progress.PendingQi[grantId] = reward.Qi;
            return true;
        }
        // 修仙侧已有标记的修为奖励：清除待对账项（不重复发放）。
        internal static bool SyncPaid(LingyuProgress progress, ISet<string> cultivationGrants)
        {
            bool changed = false;
            List<string> paid = new List<string>();
            foreach (KeyValuePair<string, long> pair in progress.PendingQi)
                if (cultivationGrants.Contains(pair.Key)) paid.Add(pair.Key);
            foreach (string id in paid) { progress.PendingQi.Remove(id); changed = true; }
            return changed;
        }
        internal static List<KeyValuePair<string, long>> PendingQiRewards(LingyuProgress progress, ISet<string> cultivationGrants)
        {
            List<KeyValuePair<string, long>> pending = new List<KeyValuePair<string, long>>();
            foreach (KeyValuePair<string, long> pair in progress.PendingQi)
                if (!cultivationGrants.Contains(pair.Key)) pending.Add(pair);
            return pending;
        }
    }

    // ---- 随机机缘框架 ----
    // 机缘按数据定义：前置条件（灵根/境界/百业道途/探索进度/历史选择）+
    // 随机几率 + 玩家选择 + 不同结果，全部经统一结算接口发放并记录存档。
    // Kind 预留 npc / realm-event / ambient 等场景；FollowUpId 串起连续机缘。
    internal sealed class JiyuanChoice
    {
        internal string Label, ResultText;
        internal LingyuReward Reward;
        internal bool Combat; // 选择后触发一场虚拟镜妖战斗，胜利才结算奖励
    }
    internal sealed class JiyuanDef
    {
        internal string Id, Name, Prompt, Kind = "realm-event";
        internal string NodeId;                 // 触发节点（realm-event）
        internal string RequiresRoot;           // 灵根条件（空为不限）
        internal int RequiresRealmIndex = -1, RequiresLayer = -1;   // 境界条件
        internal string RequiresBaiyePath; internal int RequiresBaiyeLevel; // 百业条件
        internal string RequiresPriorEncounter; // 连续机缘前置
        internal int RequiresPriorChoice = -1;
        internal int RequiresVisits = -1;       // 蜃楼完成次数
        internal double RandomChance;           // 0=条件满足即触发
        internal JiyuanChoice[] Choices = new JiyuanChoice[0];
        internal string FollowUpId = "";        // 连续机缘的下一幕（预留）
    }
    internal sealed class JiyuanContext
    {
        internal string SpiritRoot = "";
        internal int RealmIndex, Layer, MirageVisits;
        internal BaiyeProgress Baiye;
        internal LingyuProgress Lingyu;
    }
    internal static class JiyuanEngine
    {
        internal static readonly JiyuanDef[] All = {
            // 连续机缘·上：回声壁前对镜自问，玩家选择决定后续走向。
            new JiyuanDef
            {
                Id = "jing-wen-1", Name = "镜中问道·上", NodeId = "echo",
                Prompt = "回声壁上映出另一个你，镜中人问：「修行至此，所为何事？」",
                Choices = new[]
                {
                    new JiyuanChoice { Label = "对镜静坐，照见本心", ResultText = "镜中人与你一同入定，修为有所感悟", Reward = new LingyuReward { Qi = 30, Text = "修为 +30" } },
                    new JiyuanChoice { Label = "击碎镜子，破除虚妄", ResultText = "镜片碎裂，一头镜妖破镜而出！", Combat = true, Reward = new LingyuReward { Qi = 20, MaterialId = "suijing-jing", MaterialCount = 1, Text = "修为 +20 · 碎镜晶 ×1" } }
                },
                FollowUpId = "jing-wen-2"
            },
            // 连续机缘·下：前置为上的存档记录 + 境界条件；结果因上的选择而异。
            new JiyuanDef
            {
                Id = "jing-wen-2", Name = "镜中问道·下", NodeId = "atrium",
                RequiresPriorEncounter = "jing-wen-1", RequiresLayer = 12, RequiresVisits = 1,
                Prompt = "蜃楼中庭的万镜齐鸣，映照出当日回声壁前的抉择。",
                Choices = new[]
                {
                    new JiyuanChoice { Label = "直面镜中真我", ResultText = "", Reward = null } // 结果按上文选择动态生成
                }
            },
            // 随机机缘：灵根共鸣（水灵根在蜃楼入口触发）。
            new JiyuanDef
            {
                Id = "root-echo", Name = "灵根共鸣", NodeId = "ferry", Kind = "ambient",
                RequiresRoot = "water", RandomChance = 0,
                Prompt = "镜湖水汽与你体内的水灵根遥相呼应。",
                Choices = new[]
                {
                    new JiyuanChoice { Label = "凝神感应", ResultText = "水汽凝成露珠融入丹田", Reward = new LingyuReward { Qi = 15, Text = "修为 +15" } }
                }
            },
            // 随机机缘：镜面花（游历道入门后，蜃楼各节点随机出现）。
            new JiyuanDef
            {
                Id = "jing-mian-hua", Name = "镜面花", NodeId = "*", Kind = "ambient",
                RequiresBaiyePath = "youli", RequiresBaiyeLevel = 1, RandomChance = 0.15,
                Prompt = "道旁镜砖的缝隙里开出一朵半透明的花。",
                Choices = new[]
                {
                    new JiyuanChoice { Label = "采下镜花", ResultText = "镜花入手微凉", Reward = new LingyuReward { MaterialId = "jinghua", MaterialCount = 1, Qi = 8, Text = "镜花 ×1 · 修为 +8" } }
                }
            }
        };
        internal static JiyuanDef Find(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return All[i];
            return null;
        }
        // 评估某个节点（或 "*" 任意节点）当前可触发的机缘；已记录的一次性机缘不重触发。
        internal static JiyuanDef Evaluate(string nodeId, JiyuanContext ctx, Random random)
        {
            for (int i = 0; i < All.Length; i++)
            {
                JiyuanDef def = All[i];
                if (def.NodeId != "*" && def.NodeId != nodeId) continue;
                if (ctx.Lingyu.JiyuanChoices.ContainsKey(def.Id)) continue;
                if (!String.IsNullOrEmpty(def.RequiresRoot) && ctx.SpiritRoot != def.RequiresRoot) continue;
                if (def.RequiresRealmIndex >= 0 && ctx.RealmIndex < def.RequiresRealmIndex) continue;
                if (def.RequiresLayer >= 0 && ctx.Layer < def.RequiresLayer) continue;
                if (!String.IsNullOrEmpty(def.RequiresBaiyePath) &&
                    (ctx.Baiye == null || ctx.Baiye.LevelOf(def.RequiresBaiyePath) < def.RequiresBaiyeLevel)) continue;
                if (!String.IsNullOrEmpty(def.RequiresPriorEncounter))
                {
                    int prior;
                    if (!ctx.Lingyu.JiyuanChoices.TryGetValue(def.RequiresPriorEncounter, out prior)) continue;
                    if (def.RequiresPriorChoice >= 0 && prior != def.RequiresPriorChoice) continue;
                }
                if (def.RequiresVisits >= 0 && ctx.MirageVisits < def.RequiresVisits) continue;
                if (def.RandomChance > 0 && (random == null || random.NextDouble() >= def.RandomChance)) continue;
                return def;
            }
            return null;
        }
    }

    // ---- 千面蜃楼：可主动进入的秘境探索 ----
    // 状态机式节点图：入口/节点/路径选择/随机事件/战斗遭遇/奖励结算/离开机制。
    // 不设完成时限；每个转移都即时存档，可随时中断与重访。
    internal sealed class MirageEngine
    {
        private readonly LingyuProgress progress;
        private readonly Random random;
        // 控制器订阅：生成一只虚拟镜妖（绝不触碰真实系统）；胜利回调 OnCombatWon，
        // 战败/妖物消散回调 OnCombatFailed。战斗结算语义持久化在 progress.CombatOutcome。
        internal event Action<string, bool> CombatRequested;
        private JiyuanDef pendingJiyuan;

        internal MirageEngine(LingyuProgress progress, Random random)
        {
            if (progress == null) throw new ArgumentNullException("progress");
            this.progress = progress; this.random = random ?? new Random();
        }

        internal bool Active { get { return progress.ExpeditionActive; } }
        internal string NodeId { get { return progress.ExpeditionNode; } }
        internal JiyuanDef PendingJiyuan { get { return pendingJiyuan; } }
        internal bool AwaitingCombat { get { return progress.CombatPending; } }

        internal static string NodeTitle(string nodeId)
        {
            switch (nodeId)
            {
                case "ferry": return "镜湖渡口";
                case "corridor": return "镜廊深处";
                case "mist": return "雾中幽径";
                case "echo": return "回声壁";
                case "atrium": return "蜃楼中庭";
                default: return "蜃楼";
            }
        }
        internal string NodeText()
        {
            if (progress.CombatPending) return "镜妖现于桌面——击败它，蜃楼之路自会延续。";
            if (pendingJiyuan != null) return pendingJiyuan.Prompt;
            switch (progress.ExpeditionNode)
            {
                case "corridor": return "两侧镜壁映出无数重叠的身影，深处隐有微光。";
                case "mist": return "雾气自镜面渗出，五步之外不辨方向。";
                case "echo": return "一面巨大的古镜立在雾尽处，壁内隐有回声。";
                case "atrium": return "万镜悬空，镜光交错成一座辉煌中庭。蜃楼主镜就在尽头。";
                default: return "一叶扁舟泊在镜湖边，水面倒影比实景更清晰三分。";
            }
        }
        // 当前可选路径；机缘待定或战斗未决时由机缘/战斗接管选择。
        internal string[] CurrentChoices()
        {
            if (progress.CombatPending) return new string[0];
            if (pendingJiyuan != null)
            {
                string[] labels = new string[pendingJiyuan.Choices.Length];
                for (int i = 0; i < labels.Length; i++) labels[i] = pendingJiyuan.Choices[i].Label;
                return labels;
            }
            switch (progress.ExpeditionNode)
            {
                case "corridor": case "mist":
                    return new[] { "继续深入", "返回渡口", "暂离秘境（进度已保存）" };
                case "echo":
                    return new[] { "向中庭进发", "返回渡口", "暂离秘境（进度已保存）" };
                case "atrium":
                    return new[] { "直面蜃楼主镜", "返回渡口", "暂离秘境（进度已保存）" };
                default:
                    return new[] { "入左舷镜廊", "入右舷雾径", "离开秘境" };
            }
        }

        internal void Enter(JiyuanContext ctx)
        {
            progress.ExpeditionActive = true;
            if (progress.ExpeditionNode.Length == 0) progress.ExpeditionNode = "ferry";
            progress.RealmEntered.Add("qianmian");
            progress.LogLine("踏入千面蜃楼 · " + NodeTitle(progress.ExpeditionNode));
            // 重访时若战斗未决，重新生成镜妖（探索不因重启卡死）；
            // 结算语义读自持久化的 CombatOutcome，主镜战重启后仍是主镜战。
            if (progress.CombatPending) RequestCombat(progress.CombatOutcome == "final");
            else RollJiyuan(ctx);
        }
        internal void Suspend()
        {
            progress.LogLine("暂离蜃楼，进度已存");
            progress.ExpeditionActive = false;
        }
        // 重启/异常恢复专用入口：战斗未决时仅补发一次镜妖生成请求，
        // 不写进入日志、不重掷机缘、不改节点。结算语义读自持久化的
        // CombatOutcome，主镜战重启后仍是主镜战；是否真正生成、重复
        // 生成去重与敌场预算由订阅方（控制器）负责。
        internal bool ResumePendingCombat()
        {
            if (!progress.ExpeditionActive || !progress.CombatPending) return false;
            RequestCombat(progress.CombatOutcome == "final");
            return true;
        }
        internal void Choose(int index, JiyuanContext ctx)
        {
            if (progress.CombatPending) return;
            if (pendingJiyuan != null) { ResolveJiyuan(index, ctx); return; }
            string node = progress.ExpeditionNode;
            if (node == "ferry")
            {
                if (index == 0) { MoveTo("corridor"); RollNodeEvent(ctx, "corridor"); }
                else if (index == 1) { MoveTo("mist"); RollNodeEvent(ctx, "mist"); }
                else Suspend();
                return;
            }
            if (node == "corridor" || node == "mist")
            {
                if (index == 0)
                {
                    progress.ExpeditionDepth++;
                    if (progress.ExpeditionFlags.Contains(node + "-cleared") && progress.ExpeditionFlags.Contains(OtherNode(node) + "-cleared"))
                    { MoveTo("atrium"); RollNodeEvent(ctx, "atrium"); }
                    else RollNodeEvent(ctx, node);
                }
                else if (index == 1) MoveTo("ferry");
                else Suspend();
                return;
            }
            if (node == "echo")
            {
                if (index == 0)
                {
                    if (progress.ExpeditionFlags.Contains("corridor-cleared") && progress.ExpeditionFlags.Contains("mist-cleared"))
                    { MoveTo("atrium"); RollNodeEvent(ctx, "atrium"); }
                    else progress.LogLine("中庭镜门紧闭：需先平定镜廊与雾径的镜妖");
                }
                else if (index == 1) MoveTo("ferry");
                else Suspend();
                return;
            }
            if (node == "atrium")
            {
                if (index == 0) { progress.CombatOutcome = "final"; progress.CombatPending = true; RequestCombat(true); }
                else if (index == 1) MoveTo("ferry");
                else Suspend();
            }
        }
        private static string OtherNode(string node) { return node == "corridor" ? "mist" : "corridor"; }
        private void MoveTo(string node)
        {
            progress.ExpeditionNode = node;
            progress.LogLine("来到" + NodeTitle(node));
        }
        // 节点随机事件：镜像宝箱 / 镜妖袭击 / 幻术迷雾 / 发现回声壁。
        private void RollNodeEvent(JiyuanContext ctx, string node)
        {
            if (RollJiyuan(ctx)) return;
            double roll = random.NextDouble();
            if (node == "corridor")
            {
                if (roll < 0.40)
                {
                    int count = 1 + random.Next(2);
                    progress.EventCounter++;
                    LingyuSettlement.TryGrant(progress, "mirage-chest-" + progress.EventCounter.ToString(CultureInfo.InvariantCulture),
                        new LingyuReward { MaterialId = "jinghua-shuiyue", MaterialCount = count, Qi = 12, Text = "镜花水月 ×" + count + " · 修为 +12" }, DateTime.UtcNow);
                    progress.LogLine("开启镜像宝箱：镜花水月 ×" + count + " · 修为 +12");
                }
                else if (roll < 0.75) { progress.CombatOutcome = "corridor-cleared"; progress.CombatPending = true; RequestCombat(false); }
                else progress.LogLine("空镜无波，只有自己的倒影随行");
            }
            else if (node == "mist")
            {
                if (roll < 0.30)
                {
                    progress.LogLine("幻术迷雾四起，你在雾中迷失了方向（机缘：修为 +10）");
                    progress.EventCounter++;
                    LingyuSettlement.TryGrant(progress, "mirage-mist-" + progress.EventCounter.ToString(CultureInfo.InvariantCulture),
                        new LingyuReward { Qi = 10, Text = "修为 +10" }, DateTime.UtcNow);
                    MoveTo("ferry");
                }
                else if (roll < 0.60) { progress.CombatOutcome = "mist-cleared"; progress.CombatPending = true; RequestCombat(false); }
                else { MoveTo("echo"); RollNodeEvent(ctx, "echo"); }
            }
            else if (node == "echo") { /* 机缘在上面的 RollJiyuan 处理 */ }
            else if (node == "atrium") { /* 机缘与主镜由选择驱动 */ }
        }
        private bool RollJiyuan(JiyuanContext ctx)
        {
            JiyuanDef found = JiyuanEngine.Evaluate(progress.ExpeditionNode, ctx, random);
            if (found == null && progress.ExpeditionNode != "ferry") found = JiyuanEngine.Evaluate("*", ctx, random);
            pendingJiyuan = found;
            if (found != null) progress.LogLine("机缘 · " + found.Name);
            return found != null;
        }
        private void ResolveJiyuan(int index, JiyuanContext ctx)
        {
            JiyuanDef def = pendingJiyuan; pendingJiyuan = null;
            if (def == null || index < 0 || index >= def.Choices.Length) return;
            progress.JiyuanChoices[def.Id] = index;
            JiyuanChoice choice = def.Choices[index];
            LingyuReward reward = choice.Reward;
            string resultText = choice.ResultText;
            // 连续机缘·下：结果因「上」的存档选择而异。
            if (def.Id == "jing-wen-2")
            {
                int prior = progress.JiyuanChoices.ContainsKey("jing-wen-1") ? progress.JiyuanChoices["jing-wen-1"] : -1;
                if (prior == 0) { reward = new LingyuReward { ScrollId = "jingxin-scroll", Qi = 60, Text = "功法残卷「镜心残卷」 · 修为 +60" }; resultText = "镜中真人颔首，赠你半卷《镜心诀》"; }
                else { reward = new LingyuReward { MaterialId = "suijing-jing", MaterialCount = 2, Qi = 45, Text = "碎镜晶 ×2 · 修为 +45" }; resultText = "万镜碎作流光，凝成两枚锐晶"; }
            }
            if (choice.Combat)
            {
                progress.CombatOutcome = "jiyuan:" + def.Id;
                progress.CombatPending = true;
                progress.LogLine(choice.Label + "——" + resultText);
                RequestCombat(false);
                return;
            }
            GrantJiyuanReward(def, index, reward, resultText);
        }
        private void GrantJiyuanReward(JiyuanDef def, int index, LingyuReward reward, string resultText)
        {
            if (reward != null)
                LingyuSettlement.TryGrant(progress, "jiyuan-" + def.Id, reward, DateTime.UtcNow);
            progress.LogLine((resultText.Length > 0 ? resultText : def.Name) + (reward != null && reward.Text.Length > 0 ? "（" + reward.Text + "）" : ""));
        }
        private void RequestCombat(bool boss)
        {
            progress.LogLine(boss ? "蜃楼主镜碎裂，蜃楼主现身！" : "一头镜妖自镜中扑出！");
            Action<string, bool> handler = CombatRequested;
            if (handler != null) handler(boss ? "蜃楼主 · 万镜幻影" : "蜃楼镜妖", boss);
        }
        // 虚拟镜妖被击败（由控制器在战斗结算时回调）。
        // 结算语义读自持久化的 CombatOutcome：进程在战斗中重启后，重访补发的战斗
        // 胜利依旧走到这里正确结算；台账 id 幂等，重复回调不会重复发奖。
        internal void OnCombatWon(JiyuanContext ctx)
        {
            if (!progress.CombatPending) return;
            progress.CombatPending = false;
            string outcome = progress.CombatOutcome; progress.CombatOutcome = "";
            if (outcome == "corridor-cleared" || outcome == "mist-cleared")
            {
                progress.ExpeditionFlags.Add(outcome);
                progress.EventCounter++;
                LingyuSettlement.TryGrant(progress, "mirage-combat-" + progress.EventCounter.ToString(CultureInfo.InvariantCulture),
                    new LingyuReward { Qi = 40, MaterialId = "jinghua-shuiyue", MaterialCount = 1, Text = "修为 +40 · 镜花水月 ×1" }, DateTime.UtcNow);
                progress.LogLine("镜妖碎作流光（修为 +40 · 镜花水月 ×1）");
            }
            else if (outcome == "final")
            {
                progress.RealmVisits["qianmian"] = progress.Visits("qianmian") + 1;
                LingyuReward clear = new LingyuReward { Qi = 120, MaterialId = "jinghua-shuiyue", MaterialCount = 2, Text = "修为 +120 · 镜花水月 ×2" };
                if (!progress.Heritages.Contains("shuiyue-mirror")) { clear.HeritageId = "shuiyue-mirror"; clear.HeritageName = "水月镜符"; clear.Text += " · 传承「水月镜符」"; }
                LingyuSettlement.TryGrant(progress, "mirage-clear-" + progress.Visits("qianmian").ToString(CultureInfo.InvariantCulture), clear, DateTime.UtcNow);
                progress.LogLine("蜃楼主镜碎裂，蜃楼暂退——" + clear.Text);
                progress.ExpeditionFlags.Clear(); progress.ExpeditionDepth = 0;
                progress.ExpeditionNode = "ferry"; // 可继续重访
            }
            else if (outcome.StartsWith("jiyuan:", StringComparison.Ordinal))
            {
                string id = outcome.Substring(7);
                JiyuanDef def = JiyuanEngine.Find(id);
                int choiceIndex;
                if (def != null && progress.JiyuanChoices.TryGetValue(id, out choiceIndex) && choiceIndex >= 0 && choiceIndex < def.Choices.Length)
                {
                    JiyuanChoice choice = def.Choices[choiceIndex];
                    GrantJiyuanReward(def, choiceIndex, choice.Reward, choice.ResultText + "（镜妖已破）");
                }
            }
            RollJiyuan(ctx);
        }
        // 虚拟镜妖战胜角色或消散（由控制器回调）：解除战斗挂起，探索可继续；
        // 机缘战斗失败则该机缘按已选结果落空（选择已记录，奖励台账未发放）。
        internal void OnCombatFailed(JiyuanContext ctx)
        {
            if (!progress.CombatPending) return;
            progress.CombatPending = false;
            string outcome = progress.CombatOutcome; progress.CombatOutcome = "";
            if (outcome == "final") progress.LogLine("蜃楼主镜光收敛，蜃楼暂退——养精蓄锐后可再挑战");
            else if (outcome.StartsWith("jiyuan:", StringComparison.Ordinal)) progress.LogLine("镜妖遁入镜中，机缘擦肩而过");
            else progress.LogLine("镜妖遁入镜中，蜃楼之路暂缓");
            RollJiyuan(ctx);
        }
    }

    // 灵域自测：纯逻辑、注入种子随机数与合成上下文，临时目录隔离，绝不触碰真实存档。
    internal static class LingyuSelfTest
    {
        private static JiyuanContext Ctx(string root, int layer, int visits, BaiyeProgress baiye, LingyuProgress lingyu)
        {
            return new JiyuanContext { SpiritRoot = root, Layer = layer, MirageVisits = visits, Baiye = baiye, Lingyu = lingyu };
        }
        internal static string Run()
        {
            int n = 0;
            Action<bool, string> check = delegate(bool okay, string label)
            {
                if (!okay) throw new InvalidOperationException("Lingyu: " + label);
                n++;
            };
            string folder = Path.Combine(Path.GetTempPath(), "lingyu-v6-selftest-" + Guid.NewGuid().ToString("N"));
            string file = Path.Combine(folder, "lingyu-v1.txt");
            try
            {
                // 1) 名册与双途径进入条件：道途等级或境界层数，任一达标即可。
                check(LingyuCatalog.Realms.Length == 4, "four realms configured");
                check(LingyuCatalog.FindRealm("qianmian").Playable && !LingyuCatalog.FindRealm("tiangong").Playable
                    && !LingyuCatalog.FindRealm("wenqu").Playable && !LingyuCatalog.FindRealm("wanxiang").Playable,
                    "only the mirage is playable this stage, others stay as reserved interfaces");
                check(LingyuCatalog.MaterialName("jinghua-shuiyue") == "镜花水月" && LingyuCatalog.ScrollName("jingxin-scroll") == "镜心残卷"
                    && LingyuCatalog.HeritageName("shuiyue-mirror") == "水月镜符", "collection display names resolve");
                CultivationProgress cult = new CultivationProgress(); cult.EnsureIdentity(new Random(1));
                BaiyeProgress baiye = new BaiyeProgress();
                LingyuRealmDef qm = LingyuCatalog.FindRealm("qianmian");
                check(!LingyuCatalog.CanEnter(qm, baiye, cult), "mirage locked before any progress");
                baiye.AddYun("youli", 60, DateTime.UtcNow); // 游历道 L1
                check(LingyuCatalog.CanEnter(qm, baiye, cult), "dao path level unlocks regardless of cultivation");
                BaiyeProgress none = new BaiyeProgress();
                cult.Layer = 9;
                check(!LingyuCatalog.CanEnter(qm, none, cult), "one layer short stays locked");
                cult.Layer = 10;
                check(LingyuCatalog.CanEnter(qm, none, cult), "realm layer unlocks without the software ever running");
                cult.RealmIndex = 1; cult.Layer = 1;
                check(LingyuCatalog.CanEnter(qm, none, cult), "a higher realm always unlocks");
                LingyuRealmDef wx = LingyuCatalog.FindRealm("wanxiang");
                check(!LingyuCatalog.CanEnter(wx, none, cult), "wanxiang needs more than bare zhuji");
                cult.Layer = 5;
                check(LingyuCatalog.CanEnter(wx, none, cult), "wanxiang unlocks at zhuji layer 5");

                // 2) 统一结算：幂等发放 + 修为跨存档对账两个窗口。
                LingyuProgress p = new LingyuProgress();
                LingyuReward reward = new LingyuReward { Qi = 40, MaterialId = "jinghua-shuiyue", MaterialCount = 1 };
                check(LingyuSettlement.TryGrant(p, "g1", reward, DateTime.UtcNow), "first grant applies");
                check(!LingyuSettlement.TryGrant(p, "g1", reward, DateTime.UtcNow), "the same grant id is idempotent");
                check(p.Materials["jinghua-shuiyue"] == 1 && p.PendingQi["g1"] == 40 && p.Grants.ContainsKey("g1"), "material, ledger and pending qi recorded once");
                HashSet<string> cultGrants = new HashSet<string>(StringComparer.Ordinal);
                List<KeyValuePair<string, long>> due = LingyuSettlement.PendingQiRewards(p, cultGrants);
                check(due.Count == 1 && due[0].Key == "g1" && due[0].Value == 40, "an unpaid reward is pending for the cultivation side");
                cultGrants.Add("g1"); // 控制器入账并落标记
                check(LingyuSettlement.PendingQiRewards(p, cultGrants).Count == 0, "a paid reward never pays twice");
                check(LingyuSettlement.SyncPaid(p, cultGrants) && p.PendingQi.Count == 0, "sync clears the paid pending entry");
                check(!LingyuSettlement.SyncPaid(p, cultGrants), "a second sync is a no-op");
                LingyuSettlement.TryGrant(p, "g2", new LingyuReward { Qi = 10 }, DateTime.UtcNow);
                cultGrants.Add("g2"); // 窗口 b：修仙侧已有标记，灵域侧待账残留
                check(LingyuSettlement.PendingQiRewards(p, cultGrants).Count == 0 && LingyuSettlement.SyncPaid(p, cultGrants) && p.PendingQi.Count == 0,
                    "a desynced leftover clears without double paying");

                // 3) 存档：全字段往返、损坏回退、未知版本、非法行拒收、暂离保留节点。
                LingyuProgress full = new LingyuProgress();
                full.RealmVisits["qianmian"] = 2; full.RealmEntered.Add("qianmian");
                full.ExpeditionActive = true; full.ExpeditionNode = "corridor"; full.ExpeditionDepth = 3;
                full.CombatPending = true; full.CombatOutcome = "corridor-cleared";
                full.ExpeditionFlags.Add("mist-cleared"); full.LogLine("来到镜廊深处");
                full.Materials["jinghua"] = 5; full.Scrolls["jingxin-scroll"] = 1; full.Heritages.Add("shuiyue-mirror");
                full.JiyuanChoices["jing-wen-1"] = 1; full.Grants["g1"] = 123; full.PendingQi["g1"] = 40; full.EventCounter = 7;
                full.Save(file);
                LingyuProgress loaded = LingyuProgress.Load(file);
                check(loaded.Visits("qianmian") == 2 && loaded.RealmEntered.Contains("qianmian"), "visits and entered realms round trip");
                check(loaded.ExpeditionActive && loaded.ExpeditionNode == "corridor" && loaded.ExpeditionDepth == 3, "expedition state round trips");
                check(loaded.CombatPending && loaded.CombatOutcome == "corridor-cleared", "pending combat semantics survive a restart");
                check(loaded.ExpeditionFlags.Contains("mist-cleared") && loaded.ExpeditionLog.Count == 1 && loaded.ExpeditionLog[0] == "来到镜廊深处", "flags and log round trip");
                check(loaded.Materials["jinghua"] == 5 && loaded.Scrolls["jingxin-scroll"] == 1 && loaded.Heritages.Contains("shuiyue-mirror"), "collections round trip");
                check(loaded.JiyuanChoices["jing-wen-1"] == 1 && loaded.Grants["g1"] == 123 && loaded.PendingQi["g1"] == 40 && loaded.EventCounter == 7, "jiyuan archive and ledger round trip");
                full.Save(file); // 第二次保存生成滚动 .bak
                File.WriteAllText(file, "garbage-without-version", new UTF8Encoding(false));
                check(LingyuProgress.Load(file).Visits("qianmian") == 2, "a corrupt main archive falls back to the backup");
                File.WriteAllText(file, "version=99\nexpedition=1\n", new UTF8Encoding(false));
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                LingyuProgress fresh = LingyuProgress.Load(file);
                check(fresh.Visits("qianmian") == 0 && !fresh.ExpeditionActive, "an unknown future version starts fresh instead of crashing");
                File.WriteAllText(file, "version=1\nexpedition=1\nnode=bad=node\ndepth=-5\noutcome=junk=o\nvisit.unknown=3\nmat.x=-1\njiyuan.j=99\npendingqi.p=0\nheritage=bad|heritage\n", new UTF8Encoding(false));
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                LingyuProgress sane = LingyuProgress.Load(file);
                check(sane.ExpeditionNode == "" && sane.ExpeditionDepth == 0 && sane.CombatOutcome == "", "invalid tokens are dropped on load");
                check(sane.RealmVisits.Count == 0 && sane.Materials.Count == 0 && sane.JiyuanChoices.Count == 0 && sane.PendingQi.Count == 0 && sane.Heritages.Count == 0,
                    "unknown realms, negative counts and out-of-range values are dropped");
                LingyuProgress susp = new LingyuProgress();
                susp.ExpeditionActive = true; susp.ExpeditionNode = "mist"; susp.ExpeditionDepth = 2;
                MirageEngine suspEngine = new MirageEngine(susp, new Random(43));
                suspEngine.Suspend();
                susp.Save(file);
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                LingyuProgress suspBack = LingyuProgress.Load(file);
                check(!suspBack.ExpeditionActive && suspBack.ExpeditionNode == "mist" && suspBack.ExpeditionDepth == 2 && !suspBack.CombatPending,
                    "suspending keeps the node and depth for a later revisit");

                // 4) 蜃楼完整流程（种子随机、有界驱动）：渡口 → 平定镜廊与雾径 → 中庭 → 主镜 → 通关传承。
                LingyuProgress run = new LingyuProgress();
                JiyuanContext runCtx = Ctx("fire", 12, 0, new BaiyeProgress(), run);
                MirageEngine engine = new MirageEngine(run, new Random(7));
                int combats = 0, bossFights = 0; string lastLabel = "";
                engine.CombatRequested += delegate(string label, bool boss) { combats++; lastLabel = label; if (boss) bossFights++; };
                engine.Enter(runCtx);
                check(run.ExpeditionActive && run.ExpeditionNode == "ferry" && run.RealmEntered.Contains("qianmian"), "entering starts at the ferry");
                int steps = 0;
                while (run.Visits("qianmian") == 0 && steps < 300)
                {
                    steps++;
                    if (engine.PendingJiyuan != null) { engine.Choose(0, runCtx); continue; }
                    if (engine.AwaitingCombat) { engine.OnCombatWon(runCtx); continue; }
                    string node = engine.NodeId;
                    if (node == "ferry")
                        engine.Choose(run.ExpeditionFlags.Contains("corridor-cleared") && !run.ExpeditionFlags.Contains("mist-cleared") ? 1 : 0, runCtx);
                    else if (node == "echo")
                        engine.Choose(run.ExpeditionFlags.Contains("corridor-cleared") && run.ExpeditionFlags.Contains("mist-cleared") ? 0 : 1, runCtx);
                    else if (node == "corridor" || node == "mist")
                    {
                        bool ownCleared = run.ExpeditionFlags.Contains(node + "-cleared");
                        bool otherCleared = run.ExpeditionFlags.Contains(node == "corridor" ? "mist-cleared" : "corridor-cleared");
                        engine.Choose(ownCleared && !otherCleared ? 1 : 0, runCtx); // 本区已平定而彼未平：先回渡口转场
                    }
                    else engine.Choose(0, runCtx);
                }
                check(run.Visits("qianmian") == 1, "a full mirage run completes within bounded steps");
                check(run.Heritages.Contains("shuiyue-mirror"), "the first clear grants the water-mirror heritage");
                check(run.Grants.ContainsKey("mirage-clear-1") && run.PendingQi.ContainsKey("mirage-clear-1"), "the clear reward sits in the ledger awaiting settlement");
                check(run.ExpeditionNode == "ferry" && run.ExpeditionFlags.Count == 0 && run.ExpeditionActive, "clearing resets the expedition for revisits without leaving");
                check(bossFights == 1 && lastLabel == "蜃楼主 · 万镜幻影", "the final fight is the mirage lord");
                check(combats >= 3 && run.Materials.ContainsKey("jinghua-shuiyue") && run.Materials["jinghua-shuiyue"] >= 4, "node fights and the clear pay materials");
                check(run.PendingQi.Count >= 3, "qi rewards queue for cross-archive settlement");

                // 5) 战斗中断 → 保存 → 重启重访：补发战斗并按持久化语义结算。
                LingyuProgress trip = new LingyuProgress();
                trip.ExpeditionActive = true; trip.ExpeditionNode = "corridor";
                trip.CombatPending = true; trip.CombatOutcome = "corridor-cleared";
                trip.Save(file);
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                LingyuProgress reloaded = LingyuProgress.Load(file);
                check(reloaded.CombatPending && reloaded.CombatOutcome == "corridor-cleared", "an interrupted fight reloads with its outcome semantics");
                MirageEngine engB = new MirageEngine(reloaded, new Random(13));
                bool refought = false;
                engB.CombatRequested += delegate(string label, bool boss) { refought = true; };
                JiyuanContext reCtx = Ctx("fire", 12, 0, new BaiyeProgress(), reloaded);
                engB.Enter(reCtx);
                check(refought, "revisiting respawns the pending mirror fiend");
                engB.OnCombatWon(reCtx);
                check(reloaded.ExpeditionFlags.Contains("corridor-cleared") && !reloaded.CombatPending, "the reissued fight settles its node flag");
                bool combatPaid = false;
                foreach (string key in reloaded.Grants.Keys) if (key.StartsWith("mirage-combat-", StringComparison.Ordinal)) combatPaid = true;
                check(combatPaid, "the reissued fight pays its reward");
                // 主镜战中重启：补发仍是主镜战，通关奖励不丢失。
                LingyuProgress bossTrip = new LingyuProgress();
                bossTrip.ExpeditionActive = true; bossTrip.ExpeditionNode = "atrium";
                bossTrip.CombatPending = true; bossTrip.CombatOutcome = "final";
                bossTrip.Save(file);
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                LingyuProgress bossBack = LingyuProgress.Load(file);
                MirageEngine engD = new MirageEngine(bossBack, new Random(19));
                bool bossAgain = false;
                engD.CombatRequested += delegate(string label, bool boss) { if (boss) bossAgain = true; };
                JiyuanContext bossCtx = Ctx("fire", 12, 0, new BaiyeProgress(), bossBack);
                engD.Enter(bossCtx);
                check(bossAgain, "the mirage lord fight is reissued as a boss fight after a restart");
                engD.OnCombatWon(bossCtx);
                check(bossBack.Visits("qianmian") == 1 && bossBack.Heritages.Contains("shuiyue-mirror"), "the interrupted boss fight still pays the clear reward");

                // 6) 战败出口：不软锁、不发奖、探索可继续。
                LingyuProgress fail = new LingyuProgress();
                fail.ExpeditionActive = true; fail.ExpeditionNode = "corridor";
                fail.CombatPending = true; fail.CombatOutcome = "corridor-cleared";
                JiyuanContext failCtx = Ctx("fire", 12, 0, new BaiyeProgress(), fail);
                MirageEngine engE = new MirageEngine(fail, new Random(23));
                engE.CombatRequested += delegate(string label, bool boss) { };
                engE.OnCombatFailed(failCtx);
                check(!fail.CombatPending && fail.CombatOutcome.Length == 0 && !fail.ExpeditionFlags.Contains("corridor-cleared"),
                    "losing to a mirror fiend clears the pending fight without the node flag");
                check(fail.Grants.Count == 0 && engE.CurrentChoices().Length == 3, "a lost fight grants nothing and choices reopen");

                // 7) 机缘门控：灵根 / 道途 / 随机 / 一次性。
                LingyuProgress jp = new LingyuProgress();
                JiyuanContext water = Ctx("water", 1, 0, null, jp);
                JiyuanDef echo = JiyuanEngine.Evaluate("ferry", water, null);
                check(echo != null && echo.Id == "root-echo", "a water root resonates at the ferry");
                check(JiyuanEngine.Evaluate("ferry", Ctx("fire", 1, 0, null, jp), null) == null, "other roots stay silent");
                jp.JiyuanChoices["root-echo"] = 0;
                check(JiyuanEngine.Evaluate("ferry", water, null) == null, "a recorded encounter never refires");
                LingyuProgress jp2 = new LingyuProgress();
                BaiyeProgress noDao = new BaiyeProgress();
                check(JiyuanEngine.Evaluate("corridor", Ctx("fire", 12, 0, noDao, jp2), new Random(5)) == null, "the mirror flower needs the wandering dao");
                noDao.AddYun("youli", 60, DateTime.UtcNow);
                JiyuanContext daoCtx = Ctx("fire", 12, 0, noDao, jp2);
                check(JiyuanEngine.Evaluate("corridor", daoCtx, null) == null, "a null random source keeps the chance gate closed");
                bool flowerSeen = false;
                for (int seed = 0; seed < 300 && !flowerSeen; seed++)
                {
                    JiyuanDef found = JiyuanEngine.Evaluate("corridor", daoCtx, new Random(seed));
                    flowerSeen = found != null && found.Id == "jing-mian-hua";
                }
                check(flowerSeen, "the random gate opens under seeded randomness");

                // 8) 连续机缘：前置 + 境界 + 探索次数门控；结果随上文选择分叉。
                LingyuProgress chain = new LingyuProgress();
                check(JiyuanEngine.Evaluate("atrium", Ctx("fire", 12, 1, null, chain), null) == null, "the sequel needs its prequel");
                chain.JiyuanChoices["jing-wen-1"] = 0;
                JiyuanDef sequel = JiyuanEngine.Evaluate("atrium", Ctx("fire", 12, 1, null, chain), null);
                check(sequel != null && sequel.Id == "jing-wen-2", "prequel + realm + visits open the sequel");
                check(JiyuanEngine.Evaluate("atrium", Ctx("fire", 11, 1, null, chain), null) == null, "one layer short locks the sequel");
                check(JiyuanEngine.Evaluate("atrium", Ctx("fire", 12, 0, null, chain), null) == null, "never clearing the mirage locks the sequel");
                LingyuProgress chainA = new LingyuProgress();
                chainA.ExpeditionActive = true; chainA.ExpeditionNode = "atrium";
                chainA.JiyuanChoices["jing-wen-1"] = 0; chainA.RealmVisits["qianmian"] = 1;
                JiyuanContext aCtx = Ctx("fire", 12, 1, null, chainA);
                MirageEngine engF = new MirageEngine(chainA, new Random(29));
                engF.CombatRequested += delegate(string label, bool boss) { };
                engF.Enter(aCtx);
                check(engF.PendingJiyuan != null && engF.PendingJiyuan.Id == "jing-wen-2", "the sequel presents itself at the atrium");
                engF.Choose(0, aCtx);
                check(chainA.Scrolls.ContainsKey("jingxin-scroll") && chainA.PendingQi.ContainsKey("jiyuan-jing-wen-2") && chainA.PendingQi["jiyuan-jing-wen-2"] == 60,
                    "branch A pays the scroll and qi");
                LingyuProgress chainB = new LingyuProgress();
                chainB.ExpeditionActive = true; chainB.ExpeditionNode = "atrium";
                chainB.JiyuanChoices["jing-wen-1"] = 1; chainB.RealmVisits["qianmian"] = 1;
                JiyuanContext bCtx = Ctx("fire", 12, 1, null, chainB);
                MirageEngine engG = new MirageEngine(chainB, new Random(31));
                engG.CombatRequested += delegate(string label, bool boss) { };
                engG.Enter(bCtx);
                engG.Choose(0, bCtx);
                check(chainB.Materials.ContainsKey("suijing-jing") && chainB.Materials["suijing-jing"] == 2 && !chainB.Scrolls.ContainsKey("jingxin-scroll"),
                    "branch B pays crystals instead of the scroll");

                // 9) 机缘战斗分支：选择先落档，胜利按选择结算；静坐分支直接结算。
                LingyuProgress fight = new LingyuProgress();
                fight.ExpeditionActive = true; fight.ExpeditionNode = "echo";
                JiyuanContext fCtx = Ctx("fire", 12, 0, null, fight);
                MirageEngine engH = new MirageEngine(fight, new Random(37));
                int fightRequests = 0;
                engH.CombatRequested += delegate(string label, bool boss) { fightRequests++; };
                engH.Enter(fCtx);
                check(engH.PendingJiyuan != null && engH.PendingJiyuan.Id == "jing-wen-1", "the echo wall offers the prequel");
                check(engH.CurrentChoices().Length == 2, "two ways to answer the mirror");
                engH.Choose(1, fCtx); // 击碎镜子
                check(fight.CombatPending && fight.CombatOutcome == "jiyuan:jing-wen-1" && fightRequests == 1, "smashing the mirror starts a fight");
                check(fight.JiyuanChoices["jing-wen-1"] == 1, "the choice is recorded before the fight");
                engH.OnCombatWon(fCtx);
                check(fight.Grants.ContainsKey("jiyuan-jing-wen-1") && fight.Materials["suijing-jing"] == 1 && fight.PendingQi["jiyuan-jing-wen-1"] == 20,
                    "winning pays the recorded choice exactly once");
                LingyuProgress calm = new LingyuProgress();
                calm.ExpeditionActive = true; calm.ExpeditionNode = "echo";
                JiyuanContext calmCtx = Ctx("fire", 12, 0, null, calm);
                MirageEngine engI = new MirageEngine(calm, new Random(41));
                engI.CombatRequested += delegate(string label, bool boss) { throw new InvalidOperationException("no fight expected"); };
                engI.Enter(calmCtx);
                engI.Choose(0, calmCtx);
                check(calm.PendingQi.ContainsKey("jiyuan-jing-wen-1") && calm.PendingQi["jiyuan-jing-wen-1"] == 30 && !calm.CombatPending,
                    "the quiet branch pays immediately without a fight");
            }
            finally
            {
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
            }
            return "Lingyu: " + n + " checks passed (realm gating, idempotent settlement, archive fixture, mirage full run, interrupted fights, jiyuan chains).";
        }
    }
}
