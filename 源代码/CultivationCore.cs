using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NetBunny
{
    // V6's long-running cultivation model is independent of temp files, combat
    // permissions, render frame rate, and network availability.
    internal enum CultivationDeviceGrade { Unknown, ClearSpirit, Ordinary, TurbidSpirit }

    // Every progression number lives here so balance passes touch one file only.
    // No constant may reward stressing the machine or fabricating garbage files.
    internal static class CultivationBalance
    {
        internal const double BaseQiPerActiveSecond = .60;
        // Stable machine-capacity tiers; live load is never read here.
        internal const double ClearSpiritFactor = 1.0, OrdinaryFactor = 1.08, TurbidSpiritFactor = 1.18;
        // A frozen UI cannot retroactively fabricate activity: one tick credits at most this much.
        internal const double MaxCreditSecondsPerTick = 3.0;
        // Ambient virtual fiends appear on a slow timer so cultivation continues on a
        // perfectly clean machine; rewards are modest and capped by the enemy field budget.
        internal const double VirtualEncounterMinSeconds = 45, VirtualEncounterSpanSeconds = 50;
        internal const double VirtualEncounterPostponeSeconds = 15;
        internal const int VirtualKillQiMin = 6, VirtualKillQiSpan = 5;
        internal const int BossKillQiMin = 30, BossKillQiSpan = 21;
        // A defeated cultivator meditates and always recovers on their own;
        // no manual revival and no fixed online schedule is ever required.
        internal const double ReviveRestSeconds = 60;
        // ---- 凡人期：十次有效妖物击杀后开启灵根觉醒 ----
        internal const int MortalAwakenKills = 10;
        // 凡人对妖物明显弱于炼气修士；觉醒后立刻感受到强度差异。
        internal const double MortalDamageFactor = 0.75;
        // 旧存档层数超出新版每境层数时，每层折算为破境感悟（境界与灵根不动）。
        internal const int MigratedLayerInsight = 100;
        // 长期苦修契机：圆满后感悟积累到阈值即获得破境资格之一。
        internal const int InsightOpportunityThreshold = 500;
        // 炼气中期开始出现的精英妖物（更强、修为更多；纯虚拟，不碰真实系统）。
        internal const int EliteMinLayer = 6;
        internal const double EliteChance = 0.30, EliteHealthFactor = 2.5;
        internal const int EliteQiFactor = 2;
        // 千面蜃楼主掉落筑基秘宝「水月灵髓」的最低炼气层数。
        internal const int LingsuiMinLayer = 10;
    }

    // ---- 大境界配置：层数、每层修为表、瓶颈前置与突破参数全部数据化 ----
    // 各境独立配置，不是炼气数值的简单倍数；后续大境界在本表内扩展。
    internal sealed class CultivationRealmDef
    {
        internal string Id, Name;
        internal int[] LayerCosts = new int[0]; // 第 N 层 → N+1 层所需修为（可配置表）
        internal int LayersMax { get { return LayerCosts.Length + 1; } }
        internal int LayerCost(int layer)
        {
            if (layer < 1) return LayerCosts.Length > 0 ? LayerCosts[0] : Int32.MaxValue;
            int index = Math.Min(layer - 1, LayerCosts.Length - 1);
            return LayerCosts.Length > 0 ? LayerCosts[index] : Int32.MaxValue;
        }
        // 圆满瓶颈突破配置（第一版测试参数，不绑定任何固定游玩时间）。
        internal int BreakthroughBasePercent;        // 基础成功率
        internal int InsightPercentDivisor = 50;     // 每 Divisor 点破境感悟 +1% 成功率
        internal int GuaranteedInsight;              // 感悟保底阈值：达到后突破必定成功
        internal int FailureInsight = 120;           // 每次突破失败追加的感悟
        internal string MaterialId = "";             // 可选突破材料（灵域收藏），空为未配置
        internal int MaterialCount = 1;
        internal int MaterialBonusPercent;           // 消耗材料追加的成功率
        // 本境界可获得的破境契机池：破境资格绑定目标大境界，上一境界的契机
        // 不会自动充当下一境界的资格；高阶境界至少在池内保留苦修保底路径。
        internal string[] OpportunityPool = new string[0];
    }
    internal static class CultivationRealms
    {
        internal static readonly CultivationRealmDef[] Catalog = {
            new CultivationRealmDef
            {
                Id = "qi", Name = "炼气",
                LayerCosts = new[] { 120, 180, 260, 380, 550, 780, 1100, 1550, 2200, 3100, 4300 }, // 一至十二层
                BreakthroughBasePercent = 30, InsightPercentDivisor = 50, GuaranteedInsight = 2000,
                FailureInsight = 120, MaterialId = "shuiyue-lingsui", MaterialCount = 1, MaterialBonusPercent = 15,
                OpportunityPool = new[] { "treasure", "realm", "baiye", "insight" }
            },
            new CultivationRealmDef
            {
                Id = "zhuji", Name = "筑基", // 以下各大境界为第一版测试参数，随各自纵切调整
                LayerCosts = new[] { 6000, 7800, 10000, 12800, 16000, 20000, 25000, 30000 },
                BreakthroughBasePercent = 25, InsightPercentDivisor = 80, GuaranteedInsight = 8000, FailureInsight = 200,
                OpportunityPool = new[] { "realm", "baiye", "insight" } // 天材地宝仅炼气配置灵髓，不复用到高阶
            },
            new CultivationRealmDef
            {
                Id = "jindan", Name = "金丹",
                LayerCosts = new[] { 48000, 60000, 76000, 95000, 120000, 150000, 185000, 225000 },
                BreakthroughBasePercent = 22, InsightPercentDivisor = 150, GuaranteedInsight = 30000, FailureInsight = 400,
                OpportunityPool = new[] { "realm", "baiye", "insight" }
            },
            new CultivationRealmDef
            {
                Id = "yuanying", Name = "元婴",
                LayerCosts = new[] { 300000, 380000, 480000, 600000, 760000, 950000, 1200000, 1500000 },
                BreakthroughBasePercent = 18, InsightPercentDivisor = 250, GuaranteedInsight = 100000, FailureInsight = 800,
                OpportunityPool = new[] { "realm", "baiye", "insight" }
            },
            new CultivationRealmDef
            {
                Id = "huashen", Name = "化神",
                LayerCosts = new[] { 2200000, 3000000, 4200000, 6000000, 8500000, 12000000 },
                BreakthroughBasePercent = 15, InsightPercentDivisor = 400, GuaranteedInsight = 250000, FailureInsight = 1500,
                OpportunityPool = new[] { "realm", "baiye", "insight" }
            },
            new CultivationRealmDef
            {
                Id = "hedao", Name = "合道",
                LayerCosts = new[] { 18000000, 25000000, 36000000, 52000000, 75000000, 105000000 },
                BreakthroughBasePercent = 12, InsightPercentDivisor = 800, GuaranteedInsight = 800000, FailureInsight = 3000,
                OpportunityPool = new[] { "realm", "baiye", "insight" }
            },
            new CultivationRealmDef
            {
                Id = "dujie", Name = "渡劫",
                LayerCosts = new[] { 160000000, 240000000, 380000000, 600000000 },
                BreakthroughBasePercent = 10, InsightPercentDivisor = 1500, GuaranteedInsight = 2000000, FailureInsight = 6000,
                OpportunityPool = new[] { "realm", "baiye", "insight" }
            },
            new CultivationRealmDef { Id = "feisheng", Name = "飞升" } // 目前开放的最高境界
        };
        internal static CultivationRealmDef For(int realmIndex)
        {
            return Catalog[Math.Max(0, Math.Min(Catalog.Length - 1, realmIndex))];
        }
    }

    // 境界决定基础实力：凡人与各境界的战斗强度来源（功法/法宝/灵根/百业在此之上叠加）。
    internal static class CultivationCombatPower
    {
        internal static double DamageFactor(CultivationProgress progress)
        {
            if (progress == null || progress.Mortal) return CultivationBalance.MortalDamageFactor;
            // 本境层数小幅增益（每层 +5%），大境界大幅跃升（每境 +100% 基础实力）——
            // 筑基一层恒强于炼气十二层圆满，境界跨越是真跃迁而非虚名。
            return 1.0 + progress.Layer * 0.05 + progress.RealmIndex * 1.0;
        }
        // 筑基起获得实际能力：灵气护体（受击 -25%）、恢复加倍。
        internal static double DefenseFactor(CultivationProgress progress)
        {
            return progress != null && !progress.Mortal && progress.RealmIndex >= 1 ? 0.75 : 1.0;
        }
        internal static double ReviveFactor(CultivationProgress progress)
        {
            return progress != null && !progress.Mortal && progress.RealmIndex >= 1 ? 0.5 : 1.0;
        }
        internal static string SelfTest()
        {
            int n = 0;
            Action<bool, string> check = delegate(bool okay, string label)
            {
                if (!okay) throw new InvalidOperationException("Cultivation combat: " + label);
                n++;
            };
            CultivationProgress mortal = new CultivationProgress();
            check(Math.Abs(DamageFactor(mortal) - CultivationBalance.MortalDamageFactor) < 0.0001, "mortals fight at a clear disadvantage");
            CultivationProgress awakened = new CultivationProgress { Mortal = false, Layer = 1 };
            check(DamageFactor(awakened) / DamageFactor(mortal) > 1.3, "awakening is an immediately felt power jump");
            awakened.Layer = 12;
            check(DamageFactor(awakened) > DamageFactor(new CultivationProgress { Mortal = false, Layer = 6 }), "layers grow combat power");
            CultivationProgress zhuji = new CultivationProgress { Mortal = false, RealmIndex = 1, Layer = 1 };
            check(DamageFactor(zhuji) > DamageFactor(awakened) * 1.2, "a higher realm is a real strength leap");
            check(Math.Abs(DefenseFactor(zhuji) - 0.75) < 0.0001 && Math.Abs(ReviveFactor(zhuji) - 0.5) < 0.0001, "zhuji grants real abilities, not a bare title");
            check(Math.Abs(DefenseFactor(awakened) - 1.0) < 0.0001 && Math.Abs(ReviveFactor(mortal) - 1.0) < 0.0001, "mortal and qi-refining keep baseline defense");
            return "Cultivation combat: " + n + " checks passed (pure functions).";
        }
    }

    internal static class CultivationHardware
    {
        private const ulong GiB = 1024UL * 1024UL * 1024UL;

        internal static CultivationDeviceGrade Classify(int logicalProcessors, ulong totalRamBytes)
        {
            // Only stable machine capacity is used. Live load must NEVER become a
            // positive incentive to stress the machine or create garbage.
            if (logicalProcessors <= 0 || totalRamBytes == 0) return CultivationDeviceGrade.Unknown;
            if (logicalProcessors <= 4 || totalRamBytes <= 8 * GiB) return CultivationDeviceGrade.TurbidSpirit;
            if (logicalProcessors >= 12 && totalRamBytes >= 24 * GiB) return CultivationDeviceGrade.ClearSpirit;
            return CultivationDeviceGrade.Ordinary;
        }

        internal static double CultivationFactor(CultivationDeviceGrade grade)
        {
            switch (grade)
            {
                case CultivationDeviceGrade.TurbidSpirit: return CultivationBalance.TurbidSpiritFactor;
                case CultivationDeviceGrade.Ordinary: return CultivationBalance.OrdinaryFactor;
                default: return CultivationBalance.ClearSpiritFactor;
            }
        }

        internal static string GradeName(CultivationDeviceGrade grade)
        {
            switch (grade)
            {
                case CultivationDeviceGrade.TurbidSpirit: return "浊灵洞天 · 逆境历练";
                case CultivationDeviceGrade.Ordinary: return "凡灵洞天";
                case CultivationDeviceGrade.ClearSpirit: return "清灵洞天";
                default: return "洞天待鉴定";
            }
        }
    }

    // Cultivation techniques are freely switchable practice styles; none of them
    // permanently locks a dao path, and the spirit root never gates access to one.
    internal sealed class CultivationTechnique
    {
        internal string Id, Name, School, Description;
        internal double RateFactor;
        private CultivationTechnique(string id, string name, string school, double rate, string description)
        { Id = id; Name = name; School = school; RateFactor = rate; Description = description; }
        internal static readonly CultivationTechnique[] All = {
            new CultivationTechnique("breath", "引气吐纳诀", "通用", 1.0, "调匀呼吸、纳入天地灵气的基础功法，适合所有修士"),
            new CultivationTechnique("swordheart", "剑心通明诀", "剑道", 1.06, "以剑意凝神，修行略快；不妨碍日后改修他道"),
            new CultivationTechnique("spellflow", "百脉法流功", "法道", 1.06, "引导灵气贯通百脉，修行略快；不妨碍日后改修他道"),
            new CultivationTechnique("bodyforge", "锻体磐石功", "体道", 1.06, "打磨筋骨、稳守丹田，修行略快；不妨碍日后改修他道"),
            new CultivationTechnique("puppetwood", "机关偃甲术", "傀儡道", 1.06, "以神识操演机关，修行略快；不妨碍日后改修他道"),
            new CultivationTechnique("zenvision", "观星冥想篇", "通用", 1.03, "静观星象、温养神识，进境平缓而安稳")
        };
        internal static CultivationTechnique Find(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return All[i];
            return All[0];
        }
        internal static bool IsValid(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return true;
            return false;
        }
    }

    internal static class CultivationIdentity
    {
        // Spirit roots are identity flavor and a future interface, never derived from
        // hardware tier, price, or any real-world identity; they never gate techniques.
        internal static readonly string[] RootIds = { "metal", "wood", "water", "fire", "earth", "mixed" };
        internal static string RootName(string root)
        {
            switch (root)
            {
                case "metal": return "金灵根";
                case "wood": return "木灵根";
                case "water": return "水灵根";
                case "fire": return "火灵根";
                case "earth": return "土灵根";
                case "mixed": return "杂灵根";
                default: return "灵根待觉醒";
            }
        }
        internal static bool IsValidRoot(string root)
        {
            return root == "metal" || root == "wood" || root == "water" || root == "fire" || root == "earth" || root == "mixed";
        }
        internal static string RandomRoot(Random random)
        {
            if (random == null) throw new ArgumentNullException("random");
            return RootIds[random.Next(RootIds.Length)];
        }
        internal static bool IsValidPath(string path)
        {
            return path == "sword" || path == "magic" || path == "body" || path == "puppet" || path == "demon";
        }
        internal static string PathName(string path)
        {
            switch (path)
            {
                case "sword": return "剑道";
                case "magic": return "法道";
                case "body": return "体道";
                case "puppet": return "傀儡道";
                case "demon": return "魔道";
                default: return "剑道";
            }
        }
        // 觉醒时按灵根共鸣给出初始倾向与基础功法（后续可自由改修，不锁死）。
        internal static string PathForRoot(string root)
        {
            switch (root)
            {
                case "metal": return "sword";
                case "wood": return "puppet";
                case "water": return "magic";
                case "fire": return "body";
                case "earth": return "body";
                default: return "sword";
            }
        }
        internal static string TechniqueForRoot(string root)
        {
            switch (root)
            {
                case "metal": return "swordheart";
                case "wood": return "puppetwood";
                case "water": return "spellflow";
                case "fire": return "bodyforge";
                case "earth": return "bodyforge";
                default: return "breath";
            }
        }
        private static readonly string[] TitlePrefix = { "青云", "玄微", "白鹤", "听潮", "拾遗", "守拙", "凌霄", "沐阳", "归藏", "明夷", "抱朴", "见山" };
        private static readonly string[] TitleSuffix = { "子", "散人", "道人", "居士" };
        internal static string GenerateDaoTitle(Random random)
        {
            if (random == null) throw new ArgumentNullException("random");
            return TitlePrefix[random.Next(TitlePrefix.Length)] + TitleSuffix[random.Next(TitleSuffix.Length)];
        }
    }

    internal sealed class CultivationProgress
    {
        // Version 3: opp.* lines now record the realm index at which the
        // opportunity was earned (opp.<id>=<realmIndex>); v2 archives carry the
        // boolean form (opp.<id>=1) and are read as qi-refining opportunities.
        // Version 4: opp.<id>=<realmIndex>:<eventId> additionally binds the
        // earning event number (e.g. qv2 = second mirage clear), and the new
        // oppsrc.* baselines persist the last-evaluated event counters so a
        // restart can never re-credit a historical feat as a new opportunity.
        internal const int FormatVersion = 4;
        internal static readonly string[] Realms =
            { "炼气", "筑基", "金丹", "元婴", "化神", "合道", "渡劫", "飞升" };

        internal int RealmIndex;
        internal int Layer = 1;
        internal long Qi, LifetimeQi;
        internal double FractionalQi;
        internal int BreakthroughAttempts;
        internal string DaoTitle = "";
        // 凡人期：新角色从凡人开始——无灵根、无修仙功法，十次有效妖物击杀后觉醒。
        // 任何既有存档（v1/v2）都直接是修士，永不因版本升级退回凡人。
        internal bool Mortal = true;
        internal int MortalKills;
        // 凡人之躯不能存纳灵气：历练与机缘所得先记下，觉醒时一次化为修为。
        internal long DeferredQi;
        // 圆满后有效收益转化为破境感悟；感悟提高突破成功率并在阈值处保底。
        internal long BreakthroughInsight;
        // 破境契机：天材地宝 / 秘境寻宝 / 百业悟道 / 长期苦修，任一开启破境，
        // 四条路径并行不互斥；破境成功后进入下一境重新收集。
        // B2：契机绑定获得时的大境界（id → RealmIndex）——筑基机缘不能充当
        // 结丹、结婴资格，新境界的突破门槛须由新的机缘时刻重新满足。
        internal readonly Dictionary<string, int> BreakthroughOpportunities = new Dictionary<string, int>(StringComparer.Ordinal);
        // B2 延伸：每条契机同时记录来源事件编号（id → eventId，如 qv2 / ls1），
        // 与 oppsrc.* 持久化基线配合，重启后历史战绩不会被重复认定为新机缘。
        internal readonly Dictionary<string, string> OpportunityEvents = new Dictionary<string, string>(StringComparer.Ordinal);
        // 机缘评估基线（-1 = 从未评估，旧档首次评估时一次性钉住并补发迁移契机）。
        // 蜃楼通关次数 / 灵髓库存 / 百业悟道时刻，随档案持久化。
        internal int OppBaselineQianmian = -1, OppBaselineLingsui = -1;
        internal long OppBaselineBaiye = -1;
        // Empty means "not yet awakened". A saved non-mortal archive (including
        // migrated v1 archives) always carries a valid root, never overwritten here.
        internal string SpiritRoot = "";
        internal string DaoPath = "sword";
        internal string TechniqueId = "breath";
        // Unattended idle cultivation is the default; manual battle stays opt-in.
        // This toggle is independent from real maintenance consent.
        internal bool AutoBattle = true;
        // Idempotent external reward ledger (e.g. baiye resonance grants). Keys are
        // stored as grant.<id>=1 lines; older builds ignore them when reading.
        internal readonly HashSet<string> ExternalGrants = new HashSet<string>(StringComparer.Ordinal);
        internal string LastNotice = "凡俗少年拾起木剑，洞天的妖物在不远处游荡";

        internal CultivationRealmDef RealmDef { get { return CultivationRealms.For(RealmIndex); } }
        internal string RealmName { get { return Realms[Math.Max(0, Math.Min(Realms.Length - 1, RealmIndex))]; } }
        internal CultivationTechnique Technique { get { return CultivationTechnique.Find(TechniqueId); } }
        internal string RootName { get { return Mortal ? "凡俗之躯" : CultivationIdentity.RootName(SpiritRoot); } }
        internal int LayersMax { get { return RealmDef.LayersMax; } }
        internal int NextLayerCost { get { return RealmDef.LayerCost(Layer); } }
        internal bool IsFinalRealm { get { return RealmIndex >= Realms.Length - 1; } }
        internal bool RealmComplete { get { return !Mortal && Layer >= LayersMax; } }
        internal bool CanAwaken { get { return Mortal && MortalKills >= CultivationBalance.MortalAwakenKills; } }
        // 破境条件：本境圆满 + 至少一条绑定当前境界的破境契机；成功率见 BreakthroughPercent。
        internal int CurrentOpportunityCount
        {
            get
            {
                int found = 0;
                foreach (KeyValuePair<string, int> pair in BreakthroughOpportunities)
                    if (pair.Value == RealmIndex) found++;
                return found;
            }
        }
        internal bool CanAttemptBreakthrough { get { return !Mortal && !IsFinalRealm && RealmComplete && CurrentOpportunityCount > 0; } }
        // 只有绑定当前境界的契机才算已悟得；上一境界的旧契机只作历史记录。
        internal bool HasOpportunity(string id)
        {
            int earnedRealm;
            return id != null && BreakthroughOpportunities.TryGetValue(id, out earnedRealm) && earnedRealm == RealmIndex;
        }
        // 时刻授予：仅当该契机属于本境界机缘池时记录，并钉住获得时的境界。
        // 其他境界的旧记录不挡路——新境界的悟道时刻会把它刷新为当前境界。
        // eventId 为来源事件编号（蜃楼通关次数 qv<N>、灵髓库存 ls<N>、百业悟道
        // 时刻 bw<N>、感悟阈值 insight<N>），随档案持久化供审计与增量判定。
        internal bool GrantOpportunity(string id) { return GrantOpportunity(id, ""); }
        internal bool GrantOpportunity(string id, string eventId)
        {
            if (String.IsNullOrEmpty(id) || id.Length > 24 || id.IndexOfAny(new[] { '\r', '\n', '=', '|', ':' }) >= 0) return false;
            if (eventId == null) eventId = "";
            if (eventId.Length > 32 || eventId.IndexOfAny(new[] { '\r', '\n', '=', '|', ':' }) >= 0) return false;
            string[] pool = RealmDef.OpportunityPool;
            bool inPool = false;
            for (int i = 0; i < pool.Length; i++) if (pool[i] == id) { inPool = true; break; }
            if (!inPool) return false;
            int earnedRealm;
            if (BreakthroughOpportunities.TryGetValue(id, out earnedRealm) && earnedRealm == RealmIndex) return false;
            BreakthroughOpportunities[id] = RealmIndex;
            OpportunityEvents[id] = eventId;
            return true;
        }
        internal int BreakthroughPercent(bool useMaterial, bool materialAvailable)
        {
            CultivationRealmDef def = RealmDef;
            if (Mortal || IsFinalRealm || !RealmComplete) return 0;
            if (BreakthroughInsight >= def.GuaranteedInsight) return 100;
            int percent = def.BreakthroughBasePercent + (int)(BreakthroughInsight / Math.Max(1, def.InsightPercentDivisor));
            if (useMaterial && materialAvailable && def.MaterialId.Length > 0) percent += def.MaterialBonusPercent;
            return Math.Min(95, percent);
        }
        internal string Summary
        {
            get
            {
                if (Mortal)
                    return "凡人 · 凡俗历练 " + MortalKills + " / " + CultivationBalance.MortalAwakenKills +
                        (CanAwaken ? " · 灵根感应已至，可觉醒" : "") + (DeferredQi > 0 ? " · 已记下历练馈赠 " + DeferredQi : "");
                string advancement = RealmComplete ?
                    RealmName + "圆满 · 破境感悟 " + BreakthroughInsight.ToString(CultureInfo.InvariantCulture) + " · 契机 " + CurrentOpportunityCount + "/" + RealmDef.OpportunityPool.Length :
                    "修行 " + Qi.ToString(CultureInfo.InvariantCulture) + " / " + NextLayerCost.ToString(CultureInfo.InvariantCulture);
                string chance = CanAttemptBreakthrough ? " · 破境机会 " + BreakthroughPercent(false, false) + "% 起" : "";
                return (String.IsNullOrEmpty(DaoTitle) ? "" : DaoTitle + " · ") + RealmName + " · 第" + Layer + "层 · " + advancement + chance;
            }
        }

        // Identity assignment, split by archive origin:
        // - fresh mortal (no archive): no root, no dao title — both are generated
        //   by the awakening ritual after ten settled fiend kills;
        // - normal load / v1 migration (always non-mortal): saved valid values are
        //   kept verbatim and only missing or invalid fields are repaired.
        // Machine specs never decide the root, and no root or technique choice
        // is permanent.
        internal void EnsureIdentity(Random random)
        {
            if (random == null) throw new ArgumentNullException("random");
            if (Mortal) return; // 凡人身份在灵根觉醒仪式中生成
            if (String.IsNullOrEmpty(DaoTitle)) DaoTitle = CultivationIdentity.GenerateDaoTitle(random);
            if (!CultivationIdentity.IsValidRoot(SpiritRoot)) SpiritRoot = CultivationIdentity.RandomRoot(random);
            if (!CultivationTechnique.IsValid(TechniqueId)) TechniqueId = "breath";
            if (!CultivationIdentity.IsValidPath(DaoPath)) DaoPath = "sword";
        }

        // 一次成功结算的独立虚拟战斗记一次有效击杀；计数有上限，
        // 重复回调、重新扫描或异常重启都不会让计数虚增（控制器每场战斗只结算一次）。
        internal bool RecordMortalKill()
        {
            if (!Mortal || MortalKills >= CultivationBalance.MortalAwakenKills) return false;
            MortalKills++;
            LastNotice = CanAwaken ? "十次历练圆满，灵根感应已至——在修行面板完成觉醒仪式" :
                "凡俗历练 " + MortalKills + " / " + CultivationBalance.MortalAwakenKills + "，继续作战";
            return true;
        }
        // 灵根觉醒仪式：生成灵根、初始倾向与基础功法，凡人历练馈赠一次化为修为。
        internal bool Awaken(Random random)
        {
            if (random == null) throw new ArgumentNullException("random");
            if (!CanAwaken) { LastNotice = Mortal ? "历练未足，继续作战完成凡俗历练" : "灵根早已觉醒"; return false; }
            Mortal = false;
            SpiritRoot = CultivationIdentity.RandomRoot(random);
            DaoPath = CultivationIdentity.PathForRoot(SpiritRoot);
            TechniqueId = CultivationIdentity.TechniqueForRoot(SpiritRoot);
            if (String.IsNullOrEmpty(DaoTitle)) DaoTitle = CultivationIdentity.GenerateDaoTitle(random);
            RealmIndex = 0; Layer = 1; Qi = 0; FractionalQi = 0;
            long deferred = DeferredQi; DeferredQi = 0;
            LastNotice = "灵根觉醒：" + CultivationIdentity.RootName(SpiritRoot) + " · 共鸣" + CultivationIdentity.PathName(DaoPath) +
                " · 习得「" + CultivationTechnique.Find(TechniqueId).Name + "」";
            if (deferred > 0) AddQi(deferred, false); // 暂存收益获得时已计入累计历练，转化不重复计数
            return true;
        }

        // Return the number of new minor milestones. No time-based "demo end".
        // rateFactor：只读增益乘区（如洞天丹药/器灵），非法值按 1 处理，上限 4 倍。
        internal int AdvanceActiveTime(double seconds, CultivationDeviceGrade grade) { return AdvanceActiveTime(seconds, grade, 1.0); }
        internal int AdvanceActiveTime(double seconds, CultivationDeviceGrade grade, double rateFactor)
        {
            if (Mortal) return 0; // 凡人无灵根，不能吐纳灵气
            if (Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds <= 0) return 0;
            if (Double.IsNaN(rateFactor) || Double.IsInfinity(rateFactor) || rateFactor <= 0) rateFactor = 1.0;
            rateFactor = Math.Min(4.0, rateFactor);
            // A frozen UI cannot retroactively fabricate a full day of activity.
            // The controller passes elapsed real time, not a frame-count-based dt.
            double elapsed = Math.Min(CultivationBalance.MaxCreditSecondsPerTick, seconds);
            double credit = elapsed * CultivationBalance.BaseQiPerActiveSecond *
                CultivationHardware.CultivationFactor(grade) * Technique.RateFactor * rateFactor + FractionalQi;
            long whole = (long)Math.Floor(credit);
            FractionalQi = credit - whole;
            return AddQi(whole);
        }

        internal int AddQi(long awarded) { return AddQi(awarded, true); }
        // countLifetime=false：同一笔修为的形态转化（如凡人暂存→觉醒兑现），不重复计入累计历练。
        private int AddQi(long awarded, bool countLifetime)
        {
            if (awarded <= 0) return 0;
            if (countLifetime) LifetimeQi = LifetimeQi > Int64.MaxValue - awarded ? Int64.MaxValue : LifetimeQi + awarded;
            if (Mortal) { DeferredQi = DeferredQi > Int64.MaxValue - awarded ? Int64.MaxValue : DeferredQi + awarded; return 0; }
            Qi = Qi > Int64.MaxValue - awarded ? Int64.MaxValue : Qi + awarded;
            int promoted = 0;
            int maxLayer = LayersMax;
            while (Layer < maxLayer && Qi >= NextLayerCost)
            {
                Qi -= NextLayerCost;
                Layer++;
                promoted++;
            }
            // 本境圆满：继续获得的有效收益转化为破境感悟（长期苦修路径）。
            if (Layer >= maxLayer && Qi > 0)
            {
                BreakthroughInsight = BreakthroughInsight > Int64.MaxValue - Qi ? Int64.MaxValue : BreakthroughInsight + Qi;
                Qi = 0;
                if (BreakthroughInsight >= CultivationBalance.InsightOpportunityThreshold) GrantOpportunity("insight");
            }
            if (promoted > 0) LastNotice = "修为精进，达到" + RealmName + "第" + Layer + "层";
            return promoted;
        }

        // Active player decision: 圆满 + 任一破境契机后方可尝试。
        // 失败不损失境界、灵根或核心装备，只追加破境感悟；感悟达到保底阈值后必定成功。
        internal bool TryBreakthrough(Random random, bool materialUsed)
        {
            if (random == null) throw new ArgumentNullException("random");
            CultivationRealmDef def = RealmDef;
            if (!CanAttemptBreakthrough)
            {
                LastNotice = Mortal ? "凡人尚未踏入仙途，先完成灵根觉醒" :
                    IsFinalRealm ? "已达目前开放的最高境界" :
                    !RealmComplete ? "尚未圆满：修至" + RealmName + "第" + def.LayersMax + "层后方可尝试破境" :
                    "缺少破境契机：天材地宝、秘境寻宝、百业悟道、长期苦修，任得一即可开启";
                return false;
            }
            int percent = BreakthroughPercent(materialUsed, materialUsed);
            BreakthroughAttempts++;
            if (random.Next(100) < percent)
            {
                RealmIndex++;
                Layer = 1; Qi = 0; FractionalQi = 0;
                BreakthroughInsight = 0;
                BreakthroughOpportunities.Clear(); // 新境界的瓶颈需要新的契机
                OpportunityEvents.Clear();
                LastNotice = "破境成功，踏入" + RealmName + "！";
                return true;
            }
            BreakthroughInsight = BreakthroughInsight > Int64.MaxValue - def.FailureInsight ? Int64.MaxValue : BreakthroughInsight + def.FailureInsight;
            if (BreakthroughInsight >= CultivationBalance.InsightOpportunityThreshold) GrantOpportunity("insight");
            LastNotice = "本次破境未成，根基无损；破境感悟 +" + def.FailureInsight + "，后续机会更进一分";
            return false;
        }

        internal static CultivationProgress Load(string path)
        {
            CultivationProgress value;
            if (TryLoad(path, out value)) return value;
            if (TryLoad(path + ".bak", out value)) { value.LastNotice = "已从备用修炼档案恢复"; return value; }
            // 区分两种空档：从未有过档案的是新玩家（凡人开局，走十次历练觉醒）；
            // 档案存在但损坏或来自未知版本的是老玩家——保留修士身份，
            // 由 EnsureIdentity 重新感应灵根，绝不因异常读取把人打回凡人。
            bool archiveExisted = File.Exists(path) || File.Exists(path + ".bak");
            CultivationProgress fallback = new CultivationProgress { Mortal = !archiveExisted };
            if (archiveExisted) fallback.LastNotice = "修炼档案无法读取，已保留修士身份，重新感应灵根";
            return fallback;
        }

        private static bool TryLoad(string path, out CultivationProgress p)
        {
            p = null;
            if (!File.Exists(path)) return false;
            try
            {
                CultivationProgress next = new CultivationProgress();
                next.Mortal = false; // 任何既有存档默认都是修士；凡人期存档显式携带 mortal=1
                int version = -1;
                bool hasIdentity = false;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int equals = line.IndexOf('=');
                    if (equals < 1) continue;
                    string key = line.Substring(0, equals), value = line.Substring(equals + 1);
                    int n; long number; double floating;
                    if (key == "version" && Int32.TryParse(value, out n)) version = n;
                    else if (key == "realm" && Int32.TryParse(value, out n)) next.RealmIndex = n;
                    else if (key == "layer" && Int32.TryParse(value, out n)) next.Layer = n;
                    else if (key == "qi" && Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) next.Qi = number;
                    else if (key == "lifetime" && Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) next.LifetimeQi = number;
                    else if (key == "carry" && Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floating)) next.FractionalQi = floating;
                    else if (key == "attempts" && Int32.TryParse(value, out n)) next.BreakthroughAttempts = n;
                    else if (key == "mortal") next.Mortal = value == "1";
                    else if (key == "mkills" && Int32.TryParse(value, out n) && n >= 0) next.MortalKills = Math.Min(n, CultivationBalance.MortalAwakenKills);
                    else if (key == "deferred" && Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) && number >= 0) next.DeferredQi = number;
                    else if (key == "insight" && Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) && number >= 0) next.BreakthroughInsight = number;
                    else if (key == "oppsrc.qianmian" && Int32.TryParse(value, out n) && n >= -1) next.OppBaselineQianmian = Math.Min(n, 1000000);
                    else if (key == "oppsrc.lingsui" && Int32.TryParse(value, out n) && n >= -1) next.OppBaselineLingsui = Math.Min(n, 1000000);
                    else if (key == "oppsrc.baiye" && Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) && number >= -1) next.OppBaselineBaiye = number;
                    else if (key.StartsWith("opp.", StringComparison.Ordinal))
                    {
                        string opportunityId = key.Substring(4);
                        // v4 起值为 <realmIndex>:<eventId>；v3 为纯 realmIndex；
                        // v2 的 opp.<id>=1 是布尔标记：一律解释为炼气（境界0）时代获得。
                        // 读档不经 GrantOpportunity——历史契机保留原境界记录，
                        // 是否有效由 HasOpportunity/CanAttemptBreakthrough 判定。
                        string realmPart = value, eventId = "";
                        int colon = value.IndexOf(':');
                        if (colon >= 0) { realmPart = value.Substring(0, colon); eventId = value.Substring(colon + 1); }
                        int earnedRealm;
                        if (!Int32.TryParse(realmPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out earnedRealm)) earnedRealm = 0;
                        if (version < 3) { earnedRealm = 0; eventId = ""; }
                        if (opportunityId.Length > 0 && opportunityId.Length <= 24
                            && opportunityId.IndexOfAny(new[] { '\r', '\n', '=', '|', ':' }) < 0
                            && earnedRealm >= 0 && earnedRealm < Realms.Length
                            && eventId.Length <= 32 && eventId.IndexOfAny(new[] { '\r', '\n', '=', '|', ':' }) < 0)
                        {
                            next.BreakthroughOpportunities[opportunityId] = earnedRealm;
                            next.OpportunityEvents[opportunityId] = eventId;
                        }
                    }
                    else if (key == "root" && CultivationIdentity.IsValidRoot(value)) next.SpiritRoot = value;
                    else if (key == "path" && CultivationIdentity.IsValidPath(value)) next.DaoPath = value;
                    else if (key == "autobattle") next.AutoBattle = value == "1";
                    else if (key.StartsWith("grant.", StringComparison.Ordinal) && value == "1")
                    {
                        string grantId = key.Substring(6);
                        if (grantId.Length > 0 && grantId.Length <= 40 && grantId.IndexOfAny(new[] { '\r', '\n', '=', '|' }) < 0)
                            next.ExternalGrants.Add(grantId);
                    }
                    else if (key == "tech" && CultivationTechnique.IsValid(value)) { next.TechniqueId = value; hasIdentity = true; }
                    else if (key == "title" && value.Length > 0 && value.Length <= 24 && value.IndexOfAny(new[] { '\r', '\n', '=' }) < 0) { next.DaoTitle = value; hasIdentity = true; }
                }
                if ((version < 1 || version > FormatVersion) || next.RealmIndex < 0 || next.RealmIndex >= Realms.Length ||
                    next.Layer < 1 || next.Layer > 20 || next.Qi < 0 || next.LifetimeQi < 0 ||
                    next.BreakthroughAttempts < 0 || Double.IsNaN(next.FractionalQi) ||
                    Double.IsInfinity(next.FractionalQi) || next.FractionalQi < 0 || next.FractionalQi >= 1)
                    return false;
                // 旧版每境 20 层并入新层数表：超出的层数折算为破境感悟，境界与灵根不动。
                int maxLayer = next.LayersMax;
                if (next.Layer > maxLayer)
                {
                    next.BreakthroughInsight += (long)(next.Layer - maxLayer) * CultivationBalance.MigratedLayerInsight;
                    next.Layer = maxLayer;
                    next.LastNotice = "旧版层数已并入破境感悟，境界与根基无损";
                }
                // 圆满档的结余修为同样并入破境感悟（与圆满后收益转感悟同一规则）；
                // 新系统圆满档结余恒为 0，此折算只影响旧版未满层进程的结余。
                if (next.Layer >= maxLayer && next.Qi > 0)
                {
                    next.BreakthroughInsight += next.Qi;
                    next.Qi = 0;
                }
                if (next.BreakthroughInsight >= CultivationBalance.InsightOpportunityThreshold) next.GrantOpportunity("insight");
                if (next.Mortal) { /* 凡人身份等待觉醒仪式 */ }
                else if (version == 1) next.LastNotice = "修炼档案已迁移至第二版";
                else if (!hasIdentity) next.LastNotice = "修炼档案缺少身份印记，将重新感应";
                p = next; return true;
            }
            catch { return false; }
        }

        internal void Save(string path)
        {
            string folder = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            string tmp = path + ".new";
            string data = "version=" + FormatVersion + "\nrealm=" + RealmIndex + "\nlayer=" + Layer +
                "\nqi=" + Qi.ToString(CultureInfo.InvariantCulture) +
                "\nlifetime=" + LifetimeQi.ToString(CultureInfo.InvariantCulture) +
                "\ncarry=" + FractionalQi.ToString("R", CultureInfo.InvariantCulture) +
                "\nattempts=" + BreakthroughAttempts + "\nroot=" + SpiritRoot +
                "\npath=" + DaoPath + "\ntech=" + TechniqueId + "\ntitle=" + DaoTitle +
                "\nautobattle=" + (AutoBattle ? "1" : "0") +
                "\nmortal=" + (Mortal ? "1" : "0") + "\nmkills=" + MortalKills +
                "\ndeferred=" + DeferredQi.ToString(CultureInfo.InvariantCulture) +
                "\ninsight=" + BreakthroughInsight.ToString(CultureInfo.InvariantCulture) + "\n";
            foreach (KeyValuePair<string, int> opportunity in BreakthroughOpportunities)
            {
                string eventId;
                data += "opp." + opportunity.Key + "=" + opportunity.Value.ToString(CultureInfo.InvariantCulture) +
                    (OpportunityEvents.TryGetValue(opportunity.Key, out eventId) && eventId.Length > 0 ? ":" + eventId : "") + "\n";
            }
            data += "oppsrc.qianmian=" + OppBaselineQianmian + "\noppsrc.lingsui=" + OppBaselineLingsui +
                "\noppsrc.baiye=" + OppBaselineBaiye.ToString(CultureInfo.InvariantCulture) + "\n";
            foreach (string grant in ExternalGrants) data += "grant." + grant + "=1\n";
            File.WriteAllText(tmp, data, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
            else File.Move(tmp, path);
        }

        internal static string SelfTest()
        {
            int n = 0;
            Action<bool, string> check = delegate(bool okay, string label)
            {
                if (!okay) throw new InvalidOperationException("Cultivation: " + label);
                n++;
            };
            const ulong GiB = 1024UL * 1024UL * 1024UL;
            check(CultivationHardware.Classify(4, 8 * GiB) == CultivationDeviceGrade.TurbidSpirit, "low capacity cave");
            check(CultivationHardware.Classify(16, 32 * GiB) == CultivationDeviceGrade.ClearSpirit, "high capacity cave");
            check(CultivationHardware.Classify(0, 0) == CultivationDeviceGrade.Unknown, "unknown remains neutral");
            // ---- 凡人期：新角色凡人开局 ----
            CultivationProgress mortal = new CultivationProgress();
            check(mortal.Mortal && mortal.SpiritRoot == "" && mortal.MortalKills == 0 && !mortal.CanAwaken, "a fresh character starts as an un-awakened mortal");
            check(mortal.RootName == "凡俗之躯" && mortal.Summary.Contains("凡人"), "mortal stage surfaces in identity and summary");
            check(mortal.AdvanceActiveTime(60, CultivationDeviceGrade.Ordinary) == 0 && mortal.LifetimeQi == 0, "mortals cannot breathe in qi");
            mortal.AddQi(50);
            check(mortal.Qi == 0 && mortal.DeferredQi == 50 && mortal.LifetimeQi == 50, "mortal gains are deferred until awakening");
            mortal.EnsureIdentity(new Random(7));
            check(mortal.Mortal && mortal.SpiritRoot == "" && mortal.DaoTitle == "", "a mortal is never silently assigned a root");
            for (int i = 0; i < 9; i++) mortal.RecordMortalKill();
            check(mortal.MortalKills == 9 && !mortal.CanAwaken, "nine settled kills never trigger awakening early");
            mortal.RecordMortalKill();
            check(mortal.MortalKills == 10 && mortal.CanAwaken, "the tenth settled kill opens the awakening ritual");
            for (int i = 0; i < 5; i++) mortal.RecordMortalKill();
            check(mortal.MortalKills == 10, "repeated callbacks never inflate the kill count");
            long deferredBefore = mortal.DeferredQi;
            check(mortal.Awaken(new Random(11)), "the awakening ritual succeeds once opened");
            check(!mortal.Mortal && CultivationIdentity.IsValidRoot(mortal.SpiritRoot) && CultivationIdentity.IsValidPath(mortal.DaoPath) &&
                CultivationTechnique.IsValid(mortal.TechniqueId) && mortal.DaoTitle.Length > 0, "awakening assigns root, path, technique and title");
            check(mortal.DeferredQi == 0 && mortal.LifetimeQi == deferredBefore && (mortal.Qi > 0 || mortal.Layer > 1), "deferred gains convert to cultivation on awakening");
            check(!mortal.Awaken(new Random(12)), "awakening never fires twice");
            string keptRoot = mortal.SpiritRoot, keptTitle = mortal.DaoTitle;
            mortal.EnsureIdentity(new Random(9));
            check(mortal.SpiritRoot == keptRoot && mortal.DaoTitle == keptTitle, "awakened identity is never rerolled");
            // 觉醒仪式能摇出全部六种灵根
            bool[] seenRoots = new bool[CultivationIdentity.RootIds.Length];
            for (int seed = 0; seed < 100; seed++)
            {
                CultivationProgress candidate = new CultivationProgress();
                for (int kill = 0; kill < CultivationBalance.MortalAwakenKills; kill++) candidate.RecordMortalKill();
                candidate.Awaken(new Random(seed));
                for (int r = 0; r < CultivationIdentity.RootIds.Length; r++)
                    if (candidate.SpiritRoot == CultivationIdentity.RootIds[r]) seenRoots[r] = true;
            }
            for (int r = 0; r < seenRoots.Length; r++) check(seenRoots[r], "the awakening ritual reaches root " + CultivationIdentity.RootIds[r]);
            // ---- 积累速率（觉醒后） ----
            CultivationProgress slow = new CultivationProgress { Mortal = false, SpiritRoot = "water" };
            CultivationProgress fast = new CultivationProgress { Mortal = false, SpiritRoot = "fire" };
            for (int i = 0; i < 100; i++)
            {
                slow.AdvanceActiveTime(1, CultivationDeviceGrade.ClearSpirit);
                fast.AdvanceActiveTime(1, CultivationDeviceGrade.TurbidSpirit);
            }
            check(fast.LifetimeQi > slow.LifetimeQi, "tier affects long-run cultivation");
            check(slow.LifetimeQi > 0, "no temp files required");
            check(slow.AdvanceActiveTime(-1, CultivationDeviceGrade.Ordinary) == 0, "invalid delta ignored");
            check(slow.AdvanceActiveTime(Double.NaN, CultivationDeviceGrade.Ordinary) == 0, "NaN delta ignored");
            check(slow.AdvanceActiveTime(3600, CultivationDeviceGrade.Ordinary) <= 1 &&
                slow.FractionalQi <= CultivationBalance.MaxCreditSecondsPerTick * CultivationBalance.BaseQiPerActiveSecond * CultivationBalance.OrdinaryFactor,
                "a stalled tick cannot fabricate hours of cultivation");
            CultivationProgress trained = new CultivationProgress { Mortal = false, TechniqueId = "swordheart" };
            CultivationProgress plain = new CultivationProgress { Mortal = false, TechniqueId = "breath" };
            for (int i = 0; i < 1000; i++) { trained.AdvanceActiveTime(1, CultivationDeviceGrade.ClearSpirit); plain.AdvanceActiveTime(1, CultivationDeviceGrade.ClearSpirit); }
            check(trained.LifetimeQi > plain.LifetimeQi, "technique rate factor applies");
            trained.TechniqueId = "zenvision";
            check(trained.Technique.Name == "观星冥想篇", "technique switches freely without locking a path");
            // ---- 炼气一至十二层：每层按配置表消耗修为 ----
            CultivationProgress climber = new CultivationProgress { Mortal = false };
            check(climber.AddQi(119) == 0 && climber.Layer == 1 && climber.Qi == 119, "below the first configured cost stays at layer 1");
            climber.AddQi(1);
            check(climber.Layer == 2 && climber.Qi == 0, "the first layer costs exactly 120");
            int[] costs = CultivationRealms.For(0).LayerCosts;
            long total = 0; foreach (int cost in costs) total += cost;
            CultivationProgress full = new CultivationProgress { Mortal = false };
            check(full.AddQi(total) == costs.Length && full.Layer == 12 && full.Qi == 0 && full.RealmComplete, "the configured table fills all twelve qi-refining layers");
            check(full.LayersMax == 12 && CultivationRealms.For(1).LayersMax == 9 && CultivationRealms.For(1).LayerCosts[0] != 120 * 2,
                "each realm owns an independent layer table, not a multiple of qi-refining");
            // ---- 圆满：不再自动升层，收益转化为破境感悟 ----
            full.AddQi(300);
            check(full.Layer == 12 && full.Qi == 0 && full.BreakthroughInsight == 300, "a complete realm converts surplus gains to breakthrough insight");
            check(!full.HasOpportunity("insight"), "a little insight is not yet the long-cultivation opportunity");
            full.AddQi(250);
            check(full.BreakthroughInsight == 550 && full.HasOpportunity("insight"), "sustained practice earns the insight opportunity at the threshold");
            // ---- 筑基瓶颈：圆满不足以破境，必须有契机 ----
            CultivationProgress bottleneck = new CultivationProgress { Mortal = false };
            bottleneck.AddQi(total);
            check(!bottleneck.CanAttemptBreakthrough && !bottleneck.TryBreakthrough(new Random(1), false), "a complete realm alone cannot break through");
            bottleneck.GrantOpportunity("realm");
            check(bottleneck.CanAttemptBreakthrough, "any single opportunity opens the attempt");
            check(bottleneck.BreakthroughPercent(false, false) == 30, "base percent comes from the realm config");
            bottleneck.BreakthroughInsight = 500;
            check(bottleneck.BreakthroughPercent(false, false) == 40, "insight raises the percent by the configured divisor");
            check(bottleneck.BreakthroughPercent(true, true) == 55, "a consumed material adds its configured bonus");
            check(bottleneck.BreakthroughPercent(true, false) == 40, "an unavailable material adds nothing");
            bottleneck.BreakthroughInsight = 2000;
            check(bottleneck.BreakthroughPercent(false, false) == 100, "the guaranteed-insight threshold always succeeds");
            // 失败：境界灵根无损，感悟增加且提高后续机会
            CultivationProgress gambler = new CultivationProgress { Mortal = false };
            gambler.AddQi(total);
            gambler.GrantOpportunity("treasure");
            int failingSeed = -1;
            for (int seed = 1; seed < 200 && failingSeed < 0; seed++)
            {
                CultivationProgress probe = new CultivationProgress { Mortal = false };
                probe.AddQi(total); probe.GrantOpportunity("treasure");
                if (!probe.TryBreakthrough(new Random(seed), false)) failingSeed = seed;
            }
            check(failingSeed > 0, "a sub-100 percent can fail");
            gambler.BreakthroughInsight = 0;
            check(!gambler.TryBreakthrough(new Random(failingSeed), false) && gambler.RealmIndex == 0 && gambler.Layer == 12,
                "a failed attempt keeps realm, layer and root");
            check(gambler.BreakthroughInsight == 120 && gambler.HasOpportunity("treasure"), "failure adds insight and keeps the opportunity");
            // 保底：感悟阈值后必定成功；成功后契机清空、感悟归零
            gambler.BreakthroughInsight = CultivationRealms.For(0).GuaranteedInsight;
            check(gambler.TryBreakthrough(new Random(failingSeed), false) && gambler.RealmIndex == 1 && gambler.Layer == 1,
                "the pity threshold guarantees the breakthrough");
            check(gambler.BreakthroughInsight == 0 && gambler.BreakthroughOpportunities.Count == 0, "success consumes the insight and opportunities for the new realm");
            check(gambler.BreakthroughAttempts == 2, "attempts are counted");
            // ---- B2 契机按境界分层：资格绑定目标大境界 ----
            CultivationProgress layered = new CultivationProgress { Mortal = false };
            layered.AddQi(total); // 炼气圆满
            check(layered.GrantOpportunity("realm") && layered.HasOpportunity("realm") && layered.CanAttemptBreakthrough,
                "a qi-refining opportunity opens the qi-refining ritual");
            check(!layered.GrantOpportunity("made-up"), "unknown opportunity ids are refused");
            check(!layered.GrantOpportunity("realm"), "the same opportunity never grants twice in one realm");
            CultivationProgress zhuji = new CultivationProgress { Mortal = false, RealmIndex = 1, Layer = 9 };
            check(!zhuji.GrantOpportunity("treasure"), "treasure is not in the foundation-establishment pool");
            check(zhuji.GrantOpportunity("realm") && zhuji.GrantOpportunity("insight") && zhuji.CurrentOpportunityCount == 2 && zhuji.CanAttemptBreakthrough,
                "higher realms keep their own opportunity pool including the insight fallback");
            CultivationProgress stale = new CultivationProgress { Mortal = false, RealmIndex = 1, Layer = 9 };
            stale.BreakthroughOpportunities["realm"] = 0; // 炼气时代获得的秘境寻宝
            check(!stale.HasOpportunity("realm") && !stale.CanAttemptBreakthrough && stale.CurrentOpportunityCount == 0,
                "an opportunity earned in the previous realm never qualifies the next one");
            check(stale.GrantOpportunity("realm") && stale.CanAttemptBreakthrough,
                "a fresh moment in the new realm re-earns the same opportunity kind");
            // 感悟达标后凡人期灵根与境界展示不受破境影响
            check(!new CultivationProgress().TryBreakthrough(new Random(1), false), "mortals never break through");
            // ---- 身份与混合灵根保留 ----
            CultivationProgress keepMixed = new CultivationProgress { Mortal = false, SpiritRoot = "mixed", DaoTitle = "试剑子" };
            keepMixed.EnsureIdentity(new Random(5));
            check(keepMixed.SpiritRoot == "mixed" && keepMixed.DaoTitle == "试剑子", "a saved mixed root and title survive normal load");
            CultivationProgress broken = new CultivationProgress { Mortal = false, SpiritRoot = "adamant" };
            broken.EnsureIdentity(new Random(6));
            check(CultivationIdentity.IsValidRoot(broken.SpiritRoot), "corrupted root is repaired once, then persists");
            // ---- 存档：新字段往返、凡人档保持凡人、旧档升级不重置境界灵根 ----
            string testFolder = Path.Combine(Path.GetTempPath(), "cultivation-v6-selftest-" + Guid.NewGuid().ToString("N"));
            string file = Path.Combine(testFolder, "cultivation-v6.txt");
            try
            {
                gambler.Save(file);
                CultivationProgress loaded = Load(file);
                check(loaded.RealmIndex == 1 && loaded.Layer == 1 && !loaded.Mortal, "durable independent save");
                CultivationProgress mortalSave = new CultivationProgress();
                for (int kill = 0; kill < 7; kill++) mortalSave.RecordMortalKill();
                mortalSave.AddQi(88);
                mortalSave.Save(file);
                CultivationProgress mortalBack = Load(file);
                check(mortalBack.Mortal && mortalBack.MortalKills == 7 && mortalBack.DeferredQi == 88 && mortalBack.SpiritRoot == "",
                    "a mortal archive stays mortal with its kill count and deferred gains");
                mortalSave.Save(file); // 第二次保存生成 .bak
                File.WriteAllText(file, "invalid-file");
                check(Load(file).Mortal && Load(file).MortalKills == 7, "recovery from backup");
                // 旧版 v2 档：炼气 15 层 + 合法灵根 → 保留灵根与境界，层数并入感悟
                File.WriteAllText(file, "version=2\nrealm=0\nlayer=15\nqi=40\nlifetime=5000\ncarry=0\nattempts=1\nroot=wood\npath=magic\ntech=spellflow\ntitle=守拙道人\nautobattle=0\n", new UTF8Encoding(false));
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                CultivationProgress legacy = Load(file);
                check(!legacy.Mortal && legacy.SpiritRoot == "wood" && legacy.DaoTitle == "守拙道人" && legacy.RealmIndex == 0,
                    "an upgraded archive keeps its root and realm");
                check(legacy.Layer == 12 && legacy.BreakthroughInsight == 3 * CultivationBalance.MigratedLayerInsight + 40 && legacy.Qi == 0,
                    "excess legacy layers fold into insight at realm-complete");
                check(legacy.RealmComplete && !legacy.AutoBattle, "legacy realm-complete state and preferences survive");
                // 旧版 v1 档迁移
                File.WriteAllText(file, "version=1\nrealm=0\nlayer=5\nqi=12\nlifetime=300\ncarry=0.5\nattempts=0\nroot=fire\npath=magic\n", new UTF8Encoding(false));
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                CultivationProgress migrated = Load(file);
                check(!migrated.Mortal && migrated.Layer == 5 && migrated.Qi == 12 && migrated.SpiritRoot == "fire" && migrated.DaoPath == "magic" &&
                    migrated.TechniqueId == "breath" && migrated.LastNotice.Contains("迁移") && migrated.AutoBattle,
                    "version 1 archive migrates without losing progress and keeps the unattended default");
                File.WriteAllText(file, "version=99\nrealm=0\nlayer=1\n", new UTF8Encoding(false));
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                check(Load(file).Layer == 1 && Load(file).LifetimeQi == 0 && !Load(file).Mortal, "unknown future version falls back safely");
                // 感悟与契机落档往返
                CultivationProgress seasoned = new CultivationProgress { Mortal = false, Layer = 12, BreakthroughInsight = 640 };
                seasoned.GrantOpportunity("baiye"); seasoned.GrantOpportunity("insight");
                seasoned.Save(file);
                CultivationProgress seasonedBack = Load(file);
                check(seasonedBack.BreakthroughInsight == 640 && seasonedBack.HasOpportunity("baiye") && seasonedBack.HasOpportunity("insight"),
                    "insight and opportunities survive a save/load round trip");
                File.WriteAllText(file, "version=2\nrealm=0\nlayer=1\nqi=0\nlifetime=0\ncarry=0\nattempts=0\nroot=wood\npath=sword\ntech=breath\ntitle=t\nautobattle=1\nmkills=99\nopp.bad|id=1\nopp.realm=1\ngrant.bad|id=1\ngrant.good-id=1\n", new UTF8Encoding(false));
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                CultivationProgress sanitized = Load(file);
                check(sanitized.MortalKills == CultivationBalance.MortalAwakenKills && sanitized.HasOpportunity("realm") &&
                    sanitized.BreakthroughOpportunities.Count == 1 && sanitized.ExternalGrants.Contains("good-id") && sanitized.ExternalGrants.Count == 1,
                    "out-of-range kill counts and malformed keys are dropped on load");
                // B2 迁移：v2 布尔契机一律解释为炼气时代获得——筑基档里的旧契机
                // 保留为历史记录，但不充当筑基破境资格（保守迁移，不放大权限）。
                File.WriteAllText(file, "version=2\nrealm=1\nlayer=9\nqi=0\nlifetime=0\ncarry=0\nattempts=1\nroot=wood\npath=sword\ntech=breath\ntitle=t\nautobattle=1\nmkills=10\nopp.realm=1\n", new UTF8Encoding(false));
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                CultivationProgress legacyZhuji = Load(file);
                check(legacyZhuji.RealmIndex == 1 && legacyZhuji.Layer == 9 && legacyZhuji.RealmComplete
                    && legacyZhuji.BreakthroughOpportunities.Count == 1 && !legacyZhuji.HasOpportunity("realm")
                    && !legacyZhuji.CanAttemptBreakthrough,
                    "a v2 boolean opportunity stays a qi-refining record and never qualifies the next realm");
                // v3 往返：契机带着获得时的境界落档，读回分毫不差。
                CultivationProgress v3 = new CultivationProgress { Mortal = false, RealmIndex = 1, Layer = 9 };
                v3.GrantOpportunity("baiye"); v3.Save(file);
                CultivationProgress v3Back = Load(file);
                check(v3Back.BreakthroughOpportunities.Count == 1 && v3Back.HasOpportunity("baiye") && v3Back.CanAttemptBreakthrough,
                    "v3 opportunities round trip with their earning realm intact");
                // v4 往返：契机携带境界 + 事件编号，oppsrc.* 评估基线一同落档读回。
                CultivationProgress v4 = new CultivationProgress { Mortal = false, RealmIndex = 1, Layer = 9 };
                v4.GrantOpportunity("realm", "qv3"); v4.GrantOpportunity("insight", "insight2000");
                v4.OppBaselineQianmian = 3; v4.OppBaselineLingsui = 2; v4.OppBaselineBaiye = 123456789L;
                v4.Save(file);
                CultivationProgress v4Back = Load(file);
                check(v4Back.HasOpportunity("realm") && v4Back.OpportunityEvents["realm"] == "qv3"
                    && v4Back.OpportunityEvents["insight"] == "insight2000"
                    && v4Back.OppBaselineQianmian == 3 && v4Back.OppBaselineLingsui == 2 && v4Back.OppBaselineBaiye == 123456789L,
                    "v4 opportunities round trip with realm, event id and baselines intact");
                // v3 旧档（无事件编号、无基线）：读回事件编号为空、基线保持未评估。
                File.WriteAllText(file, "version=3\nrealm=0\nlayer=12\nqi=0\nlifetime=0\ncarry=0\nattempts=0\nroot=wood\npath=sword\ntech=breath\ntitle=t\nautobattle=1\nmkills=10\nopp.realm=0\n", new UTF8Encoding(false));
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                CultivationProgress v3Old = Load(file);
                check(v3Old.HasOpportunity("realm") && v3Old.OpportunityEvents["realm"] == ""
                    && v3Old.OppBaselineQianmian == -1 && v3Old.OppBaselineLingsui == -1,
                    "v3 archives load with empty event ids and unevaluated baselines");
                // 非法事件编号与带冒号的契机 id 一律拒收。
                CultivationProgress strict = new CultivationProgress { Mortal = false };
                check(!strict.GrantOpportunity("realm", "bad:id") && !strict.GrantOpportunity("rea:lm", "qv1"),
                    "malformed event ids and colons in opportunity ids are refused");
            }
            finally
            {
                try { if (Directory.Exists(testFolder)) Directory.Delete(testFolder, true); } catch { }
            }
            return "Cultivation: " + n + " checks passed (mortal stage, awakening, twelve layers, bottleneck, isolated file fixture).";
        }
    }

    // Ambient virtual fiends on a slow active-time timer: even a perfectly clean,
    // healthy machine keeps meeting wandering fiends. The scheduler never creates
    // windows or files; the controller owns spawning and the field budget caps
    // rendering pressure on low-end machines.
    internal sealed class CultivationEncounterScheduler
    {
        private readonly Random random;
        private double accumulated, nextAt;
        internal CultivationEncounterScheduler(Random random)
        {
            if (random == null) throw new ArgumentNullException("random");
            this.random = random;
            nextAt = NextInterval();
        }
        private double NextInterval()
        {
            return CultivationBalance.VirtualEncounterMinSeconds + random.NextDouble() * CultivationBalance.VirtualEncounterSpanSeconds;
        }
        // Feed the same real active seconds that cultivation itself receives.
        // Returns true once when an encounter comes due.
        internal bool Observe(double activeSeconds)
        {
            if (Double.IsNaN(activeSeconds) || Double.IsInfinity(activeSeconds) || activeSeconds <= 0) return false;
            accumulated += Math.Min(CultivationBalance.MaxCreditSecondsPerTick, activeSeconds);
            if (accumulated < nextAt) return false;
            accumulated = 0; nextAt = NextInterval();
            return true;
        }
        // The field was full when an encounter came due: retry shortly instead of
        // spawning over budget or losing the encounter entirely.
        internal void Postpone()
        {
            accumulated = 0;
            nextAt = CultivationBalance.VirtualEncounterPostponeSeconds;
        }
        internal double SecondsUntilNext { get { return Math.Max(0, nextAt - accumulated); } }
        internal int RollKillQi(bool boss)
        {
            return boss ? CultivationBalance.BossKillQiMin + random.Next(CultivationBalance.BossKillQiSpan)
                        : CultivationBalance.VirtualKillQiMin + random.Next(CultivationBalance.VirtualKillQiSpan);
        }
        internal static string SelfTest()
        {
            int n = 0;
            Action<bool, string> check = delegate(bool okay, string label)
            {
                if (!okay) throw new InvalidOperationException("Cultivation encounter: " + label);
                n++;
            };
            CultivationEncounterScheduler fresh = new CultivationEncounterScheduler(new Random(3));
            check(fresh.SecondsUntilNext >= CultivationBalance.VirtualEncounterMinSeconds &&
                fresh.SecondsUntilNext < CultivationBalance.VirtualEncounterMinSeconds + CultivationBalance.VirtualEncounterSpanSeconds, "countdown exposed for UI");
            CultivationEncounterScheduler scheduler = new CultivationEncounterScheduler(new Random(3));
            int fires = 0;
            for (int i = 0; i < 200; i++) if (scheduler.Observe(1)) fires++;
            check(fires >= 2 && fires <= 4, "encounters arrive inside the configured interval band, one per interval");
            scheduler.Postpone();
            check(scheduler.SecondsUntilNext <= CultivationBalance.VirtualEncounterPostponeSeconds, "postpone retries shortly on a full field");
            check(!scheduler.Observe(-5) && !scheduler.Observe(Double.NaN), "invalid time never advances encounters");
            for (int i = 0; i < 400 && !scheduler.Observe(1); i++) { }
            check(scheduler.SecondsUntilNext >= CultivationBalance.VirtualEncounterMinSeconds - 1, "encounter re-arms after firing");
            CultivationEncounterScheduler rewards = new CultivationEncounterScheduler(new Random(11));
            bool spread = false, bossMore = true;
            int minSeen = Int32.MaxValue, maxSeen = 0;
            for (int i = 0; i < 200; i++)
            {
                int qi = rewards.RollKillQi(false);
                minSeen = Math.Min(minSeen, qi); maxSeen = Math.Max(maxSeen, qi);
                bossMore &= rewards.RollKillQi(true) >= CultivationBalance.BossKillQiMin;
            }
            spread = minSeen == CultivationBalance.VirtualKillQiMin && maxSeen == CultivationKillMax();
            check(spread && bossMore, "kill qi rewards stay inside configured bounds");
            return "Cultivation encounter: " + n + " checks passed (pure logic; no spawn, no files).";
        }
        private static int CultivationKillMax() { return CultivationBalance.VirtualKillQiMin + CultivationBalance.VirtualKillQiSpan - 1; }
    }

    // ---- 成长节奏模拟（任务C）：纯解析模型，全部参数取自 CultivationBalance /
    // CultivationRealms / CultivationTechnique / CultivationHardware / BaiyeCatalog，
    // 不同电脑档位 × 功法配置的成长曲线可重复输出，供平衡审查（--growth-sim）。
    internal static class CultivationPacing
    {
        // 遭遇节奏假设（与调度器同源）：间隔均匀分布 45–95s，均值 70s。
        internal const double EncounterMeanSeconds =
            (CultivationBalance.VirtualEncounterMinSeconds + CultivationBalance.VirtualEncounterMinSeconds + CultivationBalance.VirtualEncounterSpanSeconds) / 2.0;
        // 击杀修为期望：普通 6..10 均匀（均值 8）；炼气六层起 30% 精英双倍 → 8×1.3=10.4。
        internal static double KillQiExpectation(int layer)
        {
            double plain = CultivationBalance.VirtualKillQiMin + (CultivationBalance.VirtualKillQiSpan - 1) / 2.0;
            return layer >= CultivationBalance.EliteMinLayer ? plain * (1 + CultivationBalance.EliteChance * (CultivationBalance.EliteQiFactor - 1)) : plain;
        }
        internal static double ActiveRate(CultivationDeviceGrade grade, string techniqueId)
        {
            return CultivationBalance.BaseQiPerActiveSecond * CultivationHardware.CultivationFactor(grade) * CultivationTechnique.Find(techniqueId).RateFactor;
        }
        // 圆满前每秒总收益：活跃吐纳 + 遭遇击杀期望。
        internal static double TotalRate(CultivationDeviceGrade grade, string techniqueId, int layer)
        {
            return ActiveRate(grade, techniqueId) + KillQiExpectation(layer) / EncounterMeanSeconds;
        }
        // 凡人一场战斗的近似时长（初始长枪 vs 一级普通妖物均值），用于十杀节奏估算。
        internal static double MortalFightSeconds()
        {
            double dps = (24.0 * CultivationBalance.MortalDamageFactor) / .58; // 星辉长枪 24 伤 / 0.58s × 凡人 0.75
            double meanHealth = 0;
            for (int i = 0; i < AdventureMonster.All.Length; i++) meanHealth += Math.Max(100, 150.0 * AdventureMonster.All[i].HealthFactor);
            meanHealth /= AdventureMonster.All.Length;
            return meanHealth / dps;
        }
        internal static double MortalTenKillsSeconds()
        {
            return CultivationBalance.MortalAwakenKills * (EncounterMeanSeconds + MortalFightSeconds());
        }
        // 本境从 1 层修至圆满（含每层耗时）的累计活跃秒数。
        internal static double RealmSeconds(CultivationDeviceGrade grade, string techniqueId, int realmIndex)
        {
            CultivationRealmDef def = CultivationRealms.For(realmIndex);
            double total = 0;
            for (int layer = 1; layer < def.LayersMax; layer++) total += def.LayerCost(layer) / TotalRate(grade, techniqueId, layer);
            return total;
        }
        internal static string Fmt(double seconds)
        {
            if (seconds < 120) return Math.Round(seconds) + " 秒";
            if (seconds < 7200) return (seconds / 60).ToString("0.0") + " 分钟";
            return (seconds / 3600).ToString("0.00") + " 小时";
        }
        internal static string Report()
        {
            StringBuilder text = new StringBuilder();
            CultivationDeviceGrade[] grades = { CultivationDeviceGrade.TurbidSpirit, CultivationDeviceGrade.Ordinary, CultivationDeviceGrade.ClearSpirit };
            string[] gradeNames = { "浊灵档（低配）", "凡灵档（标配）", "清灵档（高配）" };
            string[] techs = { "breath", "zenvision", "swordheart" };
            string[] techNames = { "引气吐纳诀 ×1.00", "观星冥想篇 ×1.03", "剑心通明诀 ×1.06" };

            text.Append("# 《桌面修仙传》V6.0 成长节奏模拟（--growth-sim 自动生成）\n\n");
            text.Append("参数同源：CultivationBalance / CultivationRealms / BaiyeCatalog；遭遇间隔均值 ")
                .Append(EncounterMeanSeconds).Append(" 秒，击杀修为期 8（炼气六层起精英期望 10.4）。\n\n");

            text.Append("## 1. 凡人十杀觉醒节奏\n\n");
            text.Append("- 单场战斗约 ").Append(MortalFightSeconds().ToString("0.0")).Append(" 秒（长枪 × 凡人 0.75 系数，普通妖物均值血量）\n");
            text.Append("- 十杀 ≈ 10 × (").Append(EncounterMeanSeconds).Append("s 遭遇 + 战斗) = **").Append(Fmt(MortalTenKillsSeconds())).Append("**\n");
            text.Append("- 觉醒即战力跃升：伤害系数 0.75 → 1.05（+40%），立刻可感知\n\n");

            text.Append("## 2. 炼气一至十二层成长曲线（累计活跃时长）\n\n");
            text.Append("| 档位 | 功法 | 吐纳 qi/s | 1→6 层 | 6→12 层 | 1→12 层合计 |\n|---|---|---|---|---|---|\n");
            CultivationRealmDef qiDef = CultivationRealms.For(0);
            for (int g = 0; g < grades.Length; g++)
                for (int t = 0; t < techs.Length; t++)
                {
                    double early = 0, late = 0;
                    for (int layer = 1; layer < qiDef.LayersMax; layer++)
                    {
                        double span = qiDef.LayerCost(layer) / TotalRate(grades[g], techs[t], layer);
                        if (layer < 6) early += span; else late += span;
                    }
                    text.Append("| ").Append(gradeNames[g]).Append(" | ").Append(techNames[t])
                        .Append(" | ").Append(ActiveRate(grades[g], techs[t]).ToString("0.000"))
                        .Append(" | ").Append(Fmt(early)).Append(" | ").Append(Fmt(late)).Append(" | **").Append(Fmt(early + late)).Append("** |\n");
                }

            text.Append("\n## 3. 炼气圆满后四条破境机缘路径\n\n");
            double fullRate = TotalRate(CultivationDeviceGrade.Ordinary, "breath", 12);
            text.Append("| 路径 | 条件 | 预计额外活跃投入（凡灵档·吐纳诀） |\n|---|---|---|\n");
            text.Append("| 长期苦修（保底） | 圆满感悟 ≥500 | ").Append(Fmt(CultivationBalance.InsightOpportunityThreshold / fullRate)).Append(" |\n");
            text.Append("| 秘境寻宝 | 通关千面蜃楼（游历道 L1 或炼气十层可进） | 蜃楼全程约 10–25 分钟（节点战 3+ 场） |\n");
            text.Append("| 天材地宝 | 炼气十层以上首胜蜃楼主得水月灵髓 | 与秘境寻宝同程，层数达标即得 |\n");
            text.Append("| 百业悟道 | 任一道途 Lv.3（360 道韵） | 60 分钟有效电脑活动 |\n");
            text.Append("\n无稀有宝物时的苦修保底链：感悟 500（资格）→ 尝试（基础 30%，失败 +120 感悟）→ 感悟 2000 保底必成 ≈ 圆满后 **")
                .Append(Fmt(CultivationRealms.For(0).GuaranteedInsight / fullRate)).Append("** 活跃时长内必定破境，机缘永不贬值（苦修只是更慢）。\n\n");

            text.Append("## 4. 筑基后与炼气期强度对比\n\n");
            CultivationProgress probe = new CultivationProgress { Mortal = false, Layer = 12 };
            double qiFactor = CultivationCombatPower.DamageFactor(probe);
            probe.RealmIndex = 1; probe.Layer = 1;
            double zjFactor = CultivationCombatPower.DamageFactor(probe);
            text.Append("- 伤害系数：炼气十二层 ").Append(qiFactor.ToString("0.00")).Append(" → 筑基一层 ").Append(zjFactor.ToString("0.00"))
                .Append("（+").Append(((zjFactor / qiFactor - 1) * 100).ToString("0")).Append("%），另有受击 -25%、恢复 ×2\n");
            text.Append("- 筑基一层升二层需 6000 修为 ≈ ").Append(Fmt(6000 / TotalRate(CultivationDeviceGrade.Ordinary, "breath", 1)))
                .Append("（凡灵档）——大境界层级节奏放缓是有意设计\n\n");

            text.Append("## 5. 判定\n\n");
            text.Append("- 档位差异 ±18% 内（低配反而略快，补偿设计），不构成付费级差距；\n");
            text.Append("- 成长曲线无数值突变：每层耗时随成本平滑上升，无长时间零反馈（遭遇与吐纳双源并行）；\n");
            text.Append("- 不设固定通关时间；高阶境界数值为第一版测试参数，随各自纵切再定稿。\n");
            return text.ToString();
        }
        internal static string SelfTest()
        {
            int n = 0;
            Action<bool, string> check = delegate(bool ok, string label) { if (!ok) throw new InvalidOperationException("Cultivation pacing: " + label); n++; };
            check(Math.Abs(EncounterMeanSeconds - 70.0) < 0.001, "encounter mean matches the configured window");
            check(KillQiExpectation(1) == 8.0 && KillQiExpectation(6) > 10.0, "elite expectation kicks in at layer six");
            check(MortalTenKillsSeconds() > 600 && MortalTenKillsSeconds() < 900, "ten mortal kills land inside a tight early-session band");
            CultivationProgress mortal = new CultivationProgress();
            CultivationProgress awakened = new CultivationProgress { Mortal = false, Layer = 1 };
            check(CultivationCombatPower.DamageFactor(awakened) / CultivationCombatPower.DamageFactor(mortal) > 1.3, "awakening power jump is immediately felt");
            check(ActiveRate(CultivationDeviceGrade.TurbidSpirit, "breath") > ActiveRate(CultivationDeviceGrade.ClearSpirit, "breath"),
                "low-end compensation stays within a narrow band");
            check(ActiveRate(CultivationDeviceGrade.TurbidSpirit, "breath") / ActiveRate(CultivationDeviceGrade.ClearSpirit, "breath") <= 1.2,
                "hardware tiers never decide the game");
            double previous = 0;
            bool monotone = true;
            CultivationRealmDef def = CultivationRealms.For(0);
            for (int layer = 1; layer < def.LayersMax; layer++)
            {
                double span = def.LayerCost(layer) / TotalRate(CultivationDeviceGrade.Ordinary, "breath", layer);
                if (span < previous) monotone = false;
                previous = span;
            }
            check(monotone, "layer times rise smoothly without sudden jumps");
            double full = RealmSeconds(CultivationDeviceGrade.Ordinary, "breath", 0);
            check(full > 3600 && full < 12 * 3600, "qi-refining completion sits in a days-scale idle band, not minutes, not weeks");
            double pity = CultivationRealms.For(0).GuaranteedInsight / TotalRate(CultivationDeviceGrade.Ordinary, "breath", 12);
            check(pity < 3600, "the no-treasure pity path completes well within an hour of surplus practice");
            CultivationProgress zhuji = new CultivationProgress { Mortal = false, RealmIndex = 1, Layer = 1 };
            check(CultivationCombatPower.DamageFactor(zhuji) > CultivationCombatPower.DamageFactor(new CultivationProgress { Mortal = false, Layer = 12 }),
                "foundation establishment outguns max qi-refining");
            return "Cultivation pacing: " + n + " checks passed (analytic model; no timers, no files).";
        }
    }
}
