using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace NetBunny
{
    // V6's long-running cultivation model is independent of temp files, combat
    // permissions, render frame rate, and network availability.
    internal enum CultivationDeviceGrade { Unknown, ClearSpirit, Ordinary, TurbidSpirit }

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
                case CultivationDeviceGrade.TurbidSpirit: return 1.18;
                case CultivationDeviceGrade.Ordinary: return 1.08;
                default: return 1.0;
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

    internal sealed class CultivationProgress
    {
        internal const int FormatVersion = 1;
        internal const double BaseQiPerActiveSecond = .60;
        internal static readonly string[] Realms =
            { "炼气", "筑基", "金丹", "元婴", "化神", "合道", "渡劫", "飞升" };

        internal int RealmIndex;
        // 1..20 are progression *milestones*, not a mandated play duration.
        // At milestone 10 the player may try to break through; at 20 it is assured.
        internal int Layer = 1;
        internal long Qi, LifetimeQi;
        internal double FractionalQi;
        internal int BreakthroughAttempts;
        internal string SpiritRoot = "mixed";
        internal string DaoPath = "sword";
        internal string LastNotice = "道种已苏醒，修士正在感应天地灵气";

        internal string RealmName { get { return Realms[Math.Max(0, Math.Min(Realms.Length - 1, RealmIndex))]; } }
        internal int NextLayerCost { get { return 60 + 25 * Layer + 60 * RealmIndex; } }
        internal int BreakthroughCost { get { return 60 + 30 * RealmIndex; } }
        internal bool IsFinalRealm { get { return RealmIndex >= Realms.Length - 1; } }
        internal int BreakthroughPercent
        {
            get { return Layer < 10 || IsFinalRealm ? 0 : Layer >= 20 ? 100 : 10 + (Layer - 10) * 9; }
        }
        internal bool CanBreakthrough { get { return BreakthroughPercent > 0 && Qi >= BreakthroughCost; } }
        internal string Summary
        {
            get
            {
                string advancement = Layer >= 20 ? "本境圆满，等待自主破境" :
                    "修行 " + Qi.ToString(CultureInfo.InvariantCulture) + " / " + NextLayerCost.ToString(CultureInfo.InvariantCulture);
                string chance = BreakthroughPercent > 0 ? " · 当前破境机会 " + BreakthroughPercent + "%" : "";
                return RealmName + " · 第" + Layer + "层 · " + advancement + chance;
            }
        }

        // Return the number of new minor milestones. No time-based "demo end".
        internal int AdvanceActiveTime(double seconds, CultivationDeviceGrade grade)
        {
            if (Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds <= 0) return 0;
            // A frozen UI cannot retroactively fabricate a full day of activity.
            // The controller passes elapsed real time, not a frame-count-based dt.
            double elapsed = Math.Min(3.0, seconds);
            double credit = elapsed * BaseQiPerActiveSecond * CultivationHardware.CultivationFactor(grade) + FractionalQi;
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
            while (Layer < 20 && Qi >= NextLayerCost)
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
                LastNotice = Layer < 10 ? "尚未达到可尝试破境的修为" :
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
            LastNotice = "本次破境未成，根基无损；可继续积累灵气或修至第20层保底";
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
                    else if (key == "root" && (value == "mixed" || value == "metal" || value == "wood" || value == "water" || value == "fire" || value == "earth")) next.SpiritRoot = value;
                    else if (key == "path" && (value == "sword" || value == "magic" || value == "body" || value == "puppet" || value == "demon")) next.DaoPath = value;
                }
                if (version != FormatVersion || next.RealmIndex < 0 || next.RealmIndex >= Realms.Length ||
                    next.Layer < 1 || next.Layer > 20 || next.Qi < 0 || next.LifetimeQi < 0 ||
                    next.BreakthroughAttempts < 0 || Double.IsNaN(next.FractionalQi) ||
                    Double.IsInfinity(next.FractionalQi) || next.FractionalQi < 0 || next.FractionalQi >= 1)
                    return false;
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
                "\npath=" + DaoPath + "\n";
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
            CultivationProgress mature = new CultivationProgress();
            check(mature.AddQi(20000) == 19 && mature.Layer == 20, "unbounded session can reach full milestone");
            check(mature.BreakthroughPercent == 100 && mature.CanBreakthrough, "layer 20 guaranteed");
            check(mature.TryBreakthrough(new Random(1)) && mature.RealmIndex == 1 && mature.Layer == 1, "explicit breakthrough");
            check(!new CultivationProgress().TryBreakthrough(new Random(1)), "no premature breakthrough");
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
            }
            finally
            {
                try { if (Directory.Exists(testFolder)) Directory.Delete(testFolder, true); } catch { }
            }
            return "Cultivation: " + n + " checks passed (isolated file fixture; no system cleanup).";
        }
    }
}
