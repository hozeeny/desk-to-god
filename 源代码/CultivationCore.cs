using System;
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
        internal const int LayersPerRealm = 20;
        internal const int LayerBaseCost = 60, LayerStepCost = 25, RealmStepCost = 60;
        internal const int BreakthroughBaseCost = 60, BreakthroughRealmCost = 30;
        internal const int BreakthroughOpenLayer = 10;
        internal const int BreakthroughBasePercent = 10, BreakthroughPerLayerPercent = 9;
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

        internal static int NextLayerCost(int realmIndex, int layer)
        {
            return LayerBaseCost + LayerStepCost * layer + RealmStepCost * Math.Max(0, realmIndex);
        }
        internal static int BreakthroughCost(int realmIndex)
        {
            return BreakthroughBaseCost + BreakthroughRealmCost * Math.Max(0, realmIndex);
        }
        internal static int BreakthroughPercent(int realmIndex, int layer, bool isFinalRealm)
        {
            if (layer < BreakthroughOpenLayer || isFinalRealm) return 0;
            if (layer >= LayersPerRealm) return 100;
            return BreakthroughBasePercent + (layer - BreakthroughOpenLayer) * BreakthroughPerLayerPercent;
        }
    }

    internal static class CultivationHardware
    {
        private const ulong GiB = 1024UL * 1024UL * 1024UL;

        internal static CultivationDeviceGrade Classify(int logicalProcessors, ulong totalRamBytes)
        {
            // Only stable machine capacity is used. Live load must NEVER become
            // a positive incentive to stress the machine or create garbage.
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
        // Version 2 adds dao title, technique and an explicit spirit-root assignment;
        // version 1 archives are migrated in place without losing any progress.
        internal const int FormatVersion = 2;
        internal static readonly string[] Realms =
            { "炼气", "筑基", "金丹", "元婴", "化神", "合道", "渡劫", "飞升" };

        internal int RealmIndex;
        // 1..20 are progression *milestones*, not a mandated play duration.
        // At milestone 10 the player may try to break through; at 20 it is assured.
        internal int Layer = 1;
        internal long Qi, LifetimeQi;
        internal double FractionalQi;
        internal int BreakthroughAttempts;
        internal string DaoTitle = "";
        // Empty means "not yet awakened": first launch must roll a real root from
        // the full table. A saved archive (including migrated v1 archives) always
        // carries a valid root, which is never overwritten here.
        internal string SpiritRoot = "";
        internal string DaoPath = "sword";
        internal string TechniqueId = "breath";
        // Unattended idle cultivation is the default; manual battle stays opt-in.
        // This toggle is independent from real maintenance consent.
        internal bool AutoBattle = true;
        internal string LastNotice = "道种已苏醒，修士正在感应天地灵气";

        internal string RealmName { get { return Realms[Math.Max(0, Math.Min(Realms.Length - 1, RealmIndex))]; } }
        internal CultivationTechnique Technique { get { return CultivationTechnique.Find(TechniqueId); } }
        internal string RootName { get { return CultivationIdentity.RootName(SpiritRoot); } }
        internal int NextLayerCost { get { return CultivationBalance.NextLayerCost(RealmIndex, Layer); } }
        internal int BreakthroughCost { get { return CultivationBalance.BreakthroughCost(RealmIndex); } }
        internal bool IsFinalRealm { get { return RealmIndex >= Realms.Length - 1; } }
        internal int BreakthroughPercent
        {
            get { return CultivationBalance.BreakthroughPercent(RealmIndex, Layer, IsFinalRealm); }
        }
        internal bool CanBreakthrough { get { return BreakthroughPercent > 0 && Qi >= BreakthroughCost; } }
        internal string Summary
        {
            get
            {
                string advancement = Layer >= CultivationBalance.LayersPerRealm ? "本境圆满，等待自主破境" :
                    "修行 " + Qi.ToString(CultureInfo.InvariantCulture) + " / " + NextLayerCost.ToString(CultureInfo.InvariantCulture);
                string chance = BreakthroughPercent > 0 ? " · 当前破境机会 " + BreakthroughPercent + "%" : "";
                return (String.IsNullOrEmpty(DaoTitle) ? "" : DaoTitle + " · ") + RealmName + " · 第" + Layer + "层 · " + advancement + chance;
            }
        }

        // Identity assignment, split by archive origin:
        // - first creation (no archive): every field falls back to a fresh roll,
        //   so a new cultivator always gets a dao title and one of the six roots;
        // - normal load / v1 migration: saved valid values are kept verbatim and
        //   only missing or invalid fields are repaired, never rerolled.
        // Machine specs never decide the root, and no root or technique choice
        // is permanent.
        internal void EnsureIdentity(Random random)
        {
            if (random == null) throw new ArgumentNullException("random");
            if (String.IsNullOrEmpty(DaoTitle)) DaoTitle = CultivationIdentity.GenerateDaoTitle(random);
            if (!CultivationIdentity.IsValidRoot(SpiritRoot)) SpiritRoot = CultivationIdentity.RandomRoot(random);
            if (!CultivationTechnique.IsValid(TechniqueId)) TechniqueId = "breath";
            if (!CultivationIdentity.IsValidPath(DaoPath)) DaoPath = "sword";
        }

        // Return the number of new minor milestones. No time-based "demo end".
        internal int AdvanceActiveTime(double seconds, CultivationDeviceGrade grade)
        {
            if (Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds <= 0) return 0;
            // A frozen UI cannot retroactively fabricate a full day of activity.
            // The controller passes elapsed real time, not a frame-count-based dt.
            double elapsed = Math.Min(CultivationBalance.MaxCreditSecondsPerTick, seconds);
            double credit = elapsed * CultivationBalance.BaseQiPerActiveSecond *
                CultivationHardware.CultivationFactor(grade) * Technique.RateFactor + FractionalQi;
            long whole = (long)Math.Floor(credit);
            FractionalQi = credit - whole;
            return AddQi(whole);
        }

        internal int AddQi(long awarded)
        {
            if (awarded <= 0) return 0;
            LifetimeQi = LifetimeQi > Int64.MaxValue - awarded ? Int64.MaxValue : LifetimeQi + awarded;
            Qi = Qi > Int64.MaxValue - awarded ? Int64.MaxValue : Qi + awarded;
            int promoted = 0;
            while (Layer < CultivationBalance.LayersPerRealm && Qi >= NextLayerCost)
            {
                Qi -= NextLayerCost;
                Layer++;
                promoted++;
            }
            if (promoted > 0) LastNotice = "修为精进，达到" + RealmName + "第" + Layer + "层";
            return promoted;
        }

        // Active player decision. A failed attempt does not destroy progress
        // or equipment; at layer 20 success is guaranteed by the design.
        internal bool TryBreakthrough(Random random)
        {
            if (random == null) throw new ArgumentNullException("random");
            if (!CanBreakthrough)
            {
                LastNotice = Layer < CultivationBalance.BreakthroughOpenLayer ? "尚未达到可尝试破境的修为" :
                    IsFinalRealm ? "已达目前开放的最高境界" : "灵气不足，继续修行后再试";
                return false;
            }
            Qi -= BreakthroughCost;
            BreakthroughAttempts++;
            if (random.Next(100) < BreakthroughPercent)
            {
                RealmIndex++;
                Layer = 1;
                LastNotice = "破境成功，踏入" + RealmName + "！";
                return true;
            }
            LastNotice = "本次破境未成，根基无损；可继续积累灵气或修至第" + CultivationBalance.LayersPerRealm + "层保底";
            return false;
        }

        internal static CultivationProgress Load(string path)
        {
            CultivationProgress value;
            if (TryLoad(path, out value)) return value;
            if (TryLoad(path + ".bak", out value)) { value.LastNotice = "已从备用修炼档案恢复"; return value; }
            return new CultivationProgress();
        }

        private static bool TryLoad(string path, out CultivationProgress p)
        {
            p = null;
            if (!File.Exists(path)) return false;
            try
            {
                CultivationProgress next = new CultivationProgress();
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
                    else if (key == "root" && CultivationIdentity.IsValidRoot(value)) next.SpiritRoot = value;
                    else if (key == "path" && CultivationIdentity.IsValidPath(value)) next.DaoPath = value;
                    else if (key == "autobattle") next.AutoBattle = value == "1";
                    else if (key == "tech" && CultivationTechnique.IsValid(value)) { next.TechniqueId = value; hasIdentity = true; }
                    else if (key == "title" && value.Length > 0 && value.Length <= 24 && value.IndexOfAny(new[] { '\r', '\n', '=' }) < 0) { next.DaoTitle = value; hasIdentity = true; }
                }
                if ((version != 1 && version != FormatVersion) || next.RealmIndex < 0 || next.RealmIndex >= Realms.Length ||
                    next.Layer < 1 || next.Layer > CultivationBalance.LayersPerRealm || next.Qi < 0 || next.LifetimeQi < 0 ||
                    next.BreakthroughAttempts < 0 || Double.IsNaN(next.FractionalQi) ||
                    Double.IsInfinity(next.FractionalQi) || next.FractionalQi < 0 || next.FractionalQi >= 1)
                    return false;
                if (version == 1) next.LastNotice = "修炼档案已迁移至第二版";
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
                "\nautobattle=" + (AutoBattle ? "1" : "0") + "\n";
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
            CultivationProgress slow = new CultivationProgress(), fast = new CultivationProgress();
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
            CultivationProgress trained = new CultivationProgress { TechniqueId = "swordheart" };
            CultivationProgress plain = new CultivationProgress { TechniqueId = "breath" };
            for (int i = 0; i < 1000; i++) { trained.AdvanceActiveTime(1, CultivationDeviceGrade.ClearSpirit); plain.AdvanceActiveTime(1, CultivationDeviceGrade.ClearSpirit); }
            check(trained.LifetimeQi > plain.LifetimeQi, "technique rate factor applies");
            trained.TechniqueId = "zenvision";
            check(trained.Technique.Name == "观星冥想篇", "technique switches freely without locking a path");
            CultivationProgress mature = new CultivationProgress();
            check(mature.AddQi(20000) == CultivationBalance.LayersPerRealm - 1 && mature.Layer == CultivationBalance.LayersPerRealm, "unbounded session can reach full milestone");
            check(mature.BreakthroughPercent == 100 && mature.CanBreakthrough, "layer 20 guaranteed");
            check(mature.TryBreakthrough(new Random(1)) && mature.RealmIndex == 1 && mature.Layer == 1, "explicit breakthrough");
            check(!new CultivationProgress().TryBreakthrough(new Random(1)), "no premature breakthrough");
            CultivationProgress identity = new CultivationProgress();
            check(identity.SpiritRoot == "" && identity.RootName == "灵根待觉醒", "fresh archive starts un-awakened, not mixed");
            identity.EnsureIdentity(new Random(7));
            check(identity.DaoTitle.Length > 0 && CultivationIdentity.IsValidRoot(identity.SpiritRoot) && CultivationTechnique.IsValid(identity.TechniqueId), "identity assigned on first launch");
            identity.EnsureIdentity(new Random(8));
            string keptTitle = identity.DaoTitle, keptRoot = identity.SpiritRoot;
            identity.EnsureIdentity(new Random(9));
            check(identity.DaoTitle == keptTitle && identity.SpiritRoot == keptRoot, "existing identity is never rerolled");
            bool[] seenRoots = new bool[CultivationIdentity.RootIds.Length];
            for (int seed = 0; seed < 100; seed++)
            {
                CultivationProgress rolled = new CultivationProgress();
                rolled.EnsureIdentity(new Random(seed));
                for (int r = 0; r < CultivationIdentity.RootIds.Length; r++)
                    if (rolled.SpiritRoot == CultivationIdentity.RootIds[r]) seenRoots[r] = true;
            }
            for (int r = 0; r < seenRoots.Length; r++) check(seenRoots[r], "random assignment reaches root " + CultivationIdentity.RootIds[r]);
            CultivationProgress keepMixed = new CultivationProgress { SpiritRoot = "mixed", DaoTitle = "试剑子" };
            keepMixed.EnsureIdentity(new Random(5));
            check(keepMixed.SpiritRoot == "mixed" && keepMixed.DaoTitle == "试剑子", "a saved mixed root and title survive normal load");
            CultivationProgress broken = new CultivationProgress { SpiritRoot = "adamant" };
            broken.EnsureIdentity(new Random(6));
            check(CultivationIdentity.IsValidRoot(broken.SpiritRoot), "corrupted root is repaired once, then persists");
            identity.DaoTitle = new string('道', 40);
            string testFolder = Path.Combine(Path.GetTempPath(), "cultivation-v6-selftest-" + Guid.NewGuid().ToString("N"));
            string file = Path.Combine(testFolder, "cultivation-v6.txt");
            try
            {
                mature.Save(file);
                CultivationProgress loaded = Load(file);
                check(loaded.RealmIndex == mature.RealmIndex && loaded.Qi == mature.Qi, "durable independent save");
                mature.AddQi(14);
                mature.Save(file);
                File.WriteAllText(file, "invalid-file");
                check(Load(file).RealmIndex == loaded.RealmIndex, "recovery from backup");
                identity.Save(file);
                check(Load(file).DaoTitle == "", "overlong dao title is rejected on load and reassigned later");
                CultivationProgress awakened = new CultivationProgress();
                awakened.EnsureIdentity(new Random(21));
                awakened.AutoBattle = false;
                awakened.Save(file);
                CultivationProgress awakenedReload = Load(file);
                check(awakenedReload.SpiritRoot == awakened.SpiritRoot && awakenedReload.DaoTitle == awakened.DaoTitle &&
                    !awakenedReload.AutoBattle, "awakened root, title and manual-battle preference survive a save/load round trip");
                File.WriteAllText(file, "version=1\nrealm=0\nlayer=5\nqi=12\nlifetime=300\ncarry=0.5\nattempts=0\nroot=fire\npath=magic\n", new UTF8Encoding(false));
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                CultivationProgress migrated = Load(file);
                check(migrated.Layer == 5 && migrated.Qi == 12 && migrated.SpiritRoot == "fire" && migrated.DaoPath == "magic" &&
                    migrated.TechniqueId == "breath" && migrated.LastNotice.Contains("迁移") && migrated.AutoBattle,
                    "version 1 archive migrates without losing progress and keeps the unattended default");
                File.WriteAllText(file, "version=99\nrealm=0\nlayer=1\n", new UTF8Encoding(false));
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                check(Load(file).Layer == 1 && Load(file).LifetimeQi == 0, "unknown future version falls back to a fresh archive instead of crashing");
            }
            finally
            {
                try { if (Directory.Exists(testFolder)) Directory.Delete(testFolder, true); } catch { }
            }
            return "Cultivation: " + n + " checks passed (isolated file fixture; no system cleanup).";
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
            spread = minSeen == CultivationBalance.VirtualKillQiMin && maxSeen == CultivationBalance.VirtualKillQiMin + CultivationBalance.VirtualKillQiSpan - 1;
            check(spread && bossMore, "kill qi rewards stay inside configured bounds");
            return "Cultivation encounter: " + n + " checks passed (pure logic; no spawn, no files).";
        }
    }
}
