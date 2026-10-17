using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NetBunny
{
    // P4 洞天经营：配方与数值全部数据化（仿 CultivationRealms.Catalog），
    // 收益温和百分比 + 限时，不开新数值赛道。时间制而非点击制——种植/炼丹/
    // 炼器都是"开始时刻 + 时长"，离线按真实流逝自然成熟，不奖励频繁上线。
    internal sealed class DongtianSeedDef { internal string Id, Name; internal long GrowSeconds; internal int Yield; }
    internal sealed class DongtianPillDef
    {
        internal string Id, Name, EffectText;
        internal long BrewSeconds;
        internal string[] Materials; // bag.<id> 各 ×1
        internal string BuffId; internal double BuffHours;
    }
    internal sealed class DongtianGearDef { internal string Id, Name, EffectText; internal long ForgeSeconds; internal string[] Materials; }

    internal static class DongtianRecipes
    {
        internal static readonly DongtianSeedDef[] Seeds =
        {
            new DongtianSeedDef { Id = "linggu", Name = "灵谷", GrowSeconds = 2 * 3600, Yield = 2 },
            new DongtianSeedDef { Id = "ningshen", Name = "凝神花", GrowSeconds = 6 * 3600, Yield = 2 },
            new DongtianSeedDef { Id = "jianxin", Name = "剑心草", GrowSeconds = 12 * 3600, Yield = 1 },
        };
        internal static readonly DongtianPillDef[] Pills =
        {
            new DongtianPillDef { Id = "juqi", Name = "聚气散", EffectText = "吐纳修为 +25% · 12 小时", BrewSeconds = 1800, Materials = new[] { "linggu", "linggu" }, BuffId = "buff-juqi", BuffHours = 12 },
            new DongtianPillDef { Id = "gushen", Name = "固神丹", EffectText = "力竭调息时长减半 · 24 小时", BrewSeconds = 1800, Materials = new[] { "ningshen", "ningshen" }, BuffId = "buff-gushen", BuffHours = 24 },
            new DongtianPillDef { Id = "wuzhen", Name = "悟真丹", EffectText = "吐纳修为 +15% · 24 小时（圆满后收益同增，感悟自然提速）", BrewSeconds = 1800, Materials = new[] { "ningshen", "linggu" }, BuffId = "buff-wuzhen", BuffHours = 24 },
            // 蜃楼「水月灵髓」的常驻用途：直接入丹，无需灵田材料。
            new DongtianPillDef { Id = "ninglu", Name = "凝露丹", EffectText = "战斗伤害 +8% · 12 小时", BrewSeconds = 1800, Materials = new[] { "shuiyue-lingsui" }, BuffId = "buff-ninglu", BuffHours = 12 },
        };
        internal static readonly DongtianGearDef[] Gears =
        {
            new DongtianGearDef { Id = "putuan", Name = "蒲团", EffectText = "吐纳修为 +3% · 常驻", ForgeSeconds = 3600, Materials = new[] { "linggu", "linggu", "linggu" } },
            new DongtianGearDef { Id = "jianwan", Name = "剑丸", EffectText = "战斗伤害 +5% · 常驻", ForgeSeconds = 3600, Materials = new[] { "jianxin", "linggu", "linggu" } },
            new DongtianGearDef { Id = "jinglu", Name = "镜炉", EffectText = "炼丹时长 −10% · 常驻", ForgeSeconds = 3600, Materials = new[] { "jianxin", "ningshen" } },
        };
        internal static DongtianSeedDef Seed(string id) { foreach (DongtianSeedDef d in Seeds) if (d.Id == id) return d; return null; }
        internal static DongtianPillDef Pill(string id) { foreach (DongtianPillDef d in Pills) if (d.Id == id) return d; return null; }
        internal static DongtianGearDef Gear(string id) { foreach (DongtianGearDef d in Gears) if (d.Id == id) return d; return null; }
        internal static string MaterialName(string id)
        {
            DongtianSeedDef s = Seed(id); if (s != null) return s.Name;
            if (id == "shuiyue-lingsui") return "水月灵髓";
            return id;
        }
    }

    // 洞天存档 dongtian-v1.txt：独立、版本化、幂等台账。材料入库经
    // dtin-<mat>-<seq> 双台账（洞天档 + 灵域档），启动对账双向补平——
    // 任一档写入失败都不会让材料复制或消失（协议同 B1 破境材料事务）。
    internal sealed class DongtianProgress
    {
        internal const int FormatVersion = 1;
        internal const int MaxFields = 4;
        internal int Level = 1;
        // 灵田/丹炉/器位：<recipeId>,<readyUtcTicks>；空位为 ""。
        internal readonly string[] Fields = new string[MaxFields];
        internal string Furnace, Forge; // null = 空位
        internal readonly Dictionary<string, int> Bag = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly Dictionary<string, long> Buffs = new Dictionary<string, long>(StringComparer.Ordinal); // buffId → 到期 UtcTicks
        internal readonly HashSet<string> Gears = new HashSet<string>(StringComparer.Ordinal); // 已铸器灵（常驻，非堆叠）
        internal readonly HashSet<string> Grants = new HashSet<string>(StringComparer.Ordinal);
        internal int ImportSeq; // 入库序号（幂等台账用）
        internal string LastNotice = "";

        internal int UnlockedFields { get { return Math.Min(MaxFields, 1 + Level); } }
        internal bool HasGear(string id) { return Gears.Contains(id); }
        internal int CountOf(string material) { int n; return Bag.TryGetValue(material, out n) ? n : 0; }

        internal bool BuffActive(string buffId, DateTime utc)
        {
            long expires; return Buffs.TryGetValue(buffId, out expires) && expires > utc.Ticks;
        }
        // —— 增益修饰器（只读乘区，不写 CultivationProgress 本体）——
        internal double QiRateFactor(DateTime utc)
        {
            double f = 1.0;
            if (BuffActive("buff-juqi", utc)) f += 0.25;
            if (BuffActive("buff-wuzhen", utc)) f += 0.15;
            if (HasGear("putuan")) f += 0.03;
            return f;
        }
        internal double DamageFactor(DateTime utc)
        {
            double f = 1.0;
            if (BuffActive("buff-ninglu", utc)) f += 0.08;
            if (HasGear("jianwan")) f += 0.05;
            return f;
        }
        internal double ReviveTimeFactor(DateTime utc) { return BuffActive("buff-gushen", utc) ? 0.5 : 1.0; }
        internal double FurnaceTimeFactor { get { return HasGear("jinglu") ? 0.9 : 1.0; } }

        private static string Slot(string recipe, long readyUtc) { return recipe + "," + readyUtc.ToString(CultureInfo.InvariantCulture); }
        private static bool ParseSlot(string raw, out string recipe, out long readyUtc)
        {
            recipe = ""; readyUtc = 0;
            if (String.IsNullOrEmpty(raw)) return false;
            int comma = raw.IndexOf(',');
            if (comma < 1 || !Int64.TryParse(raw.Substring(comma + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out readyUtc)) return false;
            recipe = raw.Substring(0, comma);
            return recipe.Length > 0 && recipe.Length <= 24 && recipe.IndexOfAny(new[] { '\r', '\n', '=', '|' }) < 0 && readyUtc > 0;
        }

        // —— 灵田 ——
        internal bool Plant(int slot, string seedId, DateTime utc)
        {
            DongtianSeedDef seed = DongtianRecipes.Seed(seedId);
            if (seed == null || slot < 0 || slot >= UnlockedFields || Fields[slot] != null) return false;
            Fields[slot] = Slot(seed.Id, utc.Ticks + seed.GrowSeconds * TimeSpan.TicksPerSecond);
            LastNotice = "灵田播下「" + seed.Name + "」，" + seed.GrowSeconds / 3600 + " 时辰后成熟";
            return true;
        }
        internal bool FieldReady(int slot, DateTime utc)
        {
            string recipe; long ready;
            return slot >= 0 && slot < MaxFields && Fields[slot] != null && ParseSlot(Fields[slot], out recipe, out ready) && utc.Ticks >= ready;
        }
        internal bool Harvest(int slot, DateTime utc)
        {
            string recipe; long ready;
            if (!FieldReady(slot, utc) || !ParseSlot(Fields[slot], out recipe, out ready)) return false;
            DongtianSeedDef seed = DongtianRecipes.Seed(recipe);
            if (seed == null) { Fields[slot] = null; return false; }
            Bag[seed.Id] = CountOf(seed.Id) + seed.Yield;
            Fields[slot] = null;
            LastNotice = "灵田收获「" + seed.Name + "」×" + seed.Yield + "，已入洞天储物";
            return true;
        }
        // —— 丹炉 ——
        internal bool StartPill(string pillId, DateTime utc)
        {
            DongtianPillDef pill = DongtianRecipes.Pill(pillId);
            if (pill == null || Furnace != null) return false;
            if (!Consume(pill.Materials)) return false;
            Furnace = Slot(pill.Id, utc.Ticks + (long)(pill.BrewSeconds * FurnaceTimeFactor) * TimeSpan.TicksPerSecond);
            LastNotice = "丹炉开火，炼制「" + pill.Name + "」";
            return true;
        }
        internal bool FurnaceReady(DateTime utc) { string r; long t; return Furnace != null && ParseSlot(Furnace, out r, out t) && utc.Ticks >= t; }
        // 收丹即服：限时增益立即生效（同系续期取大，不叠乘）。
        internal bool CollectPill(DateTime utc)
        {
            string recipe; long ready;
            if (!FurnaceReady(utc) || !ParseSlot(Furnace, out recipe, out ready)) return false;
            DongtianPillDef pill = DongtianRecipes.Pill(recipe);
            Furnace = null;
            if (pill == null) return false;
            long expires = utc.Ticks + (long)(pill.BuffHours * 3600) * TimeSpan.TicksPerSecond;
            long old; if (Buffs.TryGetValue(pill.BuffId, out old) && old > expires) expires = old;
            Buffs[pill.BuffId] = expires;
            LastNotice = "「" + pill.Name + "」丹成即服：" + pill.EffectText;
            return true;
        }
        // —— 器位 ——
        internal bool StartGear(string gearId, DateTime utc)
        {
            DongtianGearDef gear = DongtianRecipes.Gear(gearId);
            if (gear == null || Forge != null || Gears.Contains(gear.Id)) return false;
            if (!Consume(gear.Materials)) return false;
            Forge = Slot(gear.Id, utc.Ticks + gear.ForgeSeconds * TimeSpan.TicksPerSecond);
            LastNotice = "器位开炼「" + gear.Name + "」";
            return true;
        }
        internal bool ForgeReady(DateTime utc) { string r; long t; return Forge != null && ParseSlot(Forge, out r, out t) && utc.Ticks >= t; }
        internal bool CollectGear(DateTime utc)
        {
            string recipe; long ready;
            if (!ForgeReady(utc) || !ParseSlot(Forge, out recipe, out ready)) return false;
            DongtianGearDef gear = DongtianRecipes.Gear(recipe);
            Forge = null;
            if (gear == null) return false;
            Gears.Add(gear.Id);
            LastNotice = "器灵「" + gear.Name + "」铸成常驻：" + gear.EffectText;
            return true;
        }
        private bool Consume(string[] materials)
        {
            Dictionary<string, int> need = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string m in materials) need[m] = (need.ContainsKey(m) ? need[m] : 0) + 1;
            foreach (KeyValuePair<string, int> pair in need) if (CountOf(pair.Key) < pair.Value) { LastNotice = "材料不足：" + DongtianRecipes.MaterialName(pair.Key) + " ×" + pair.Value; return false; }
            foreach (KeyValuePair<string, int> pair in need)
            {
                Bag[pair.Key] -= pair.Value;
                if (Bag[pair.Key] <= 0) Bag.Remove(pair.Key);
            }
            return true;
        }

        // —— 存档 ——
        internal void Save(string path)
        {
            string folder = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            StringBuilder data = new StringBuilder();
            data.Append("version=").Append(FormatVersion).Append("\nlevel=").Append(Level)
                .Append("\nfurnace=").Append(Furnace ?? "").Append("\nforge=").Append(Forge ?? "")
                .Append("\nimportseq=").Append(ImportSeq).Append('\n');
            for (int i = 0; i < MaxFields; i++) if (Fields[i] != null) data.Append("field.").Append(i).Append('=').Append(Fields[i]).Append('\n');
            foreach (KeyValuePair<string, int> pair in Bag) data.Append("bag.").Append(pair.Key).Append('=').Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (KeyValuePair<string, long> pair in Buffs) data.Append("buff.").Append(pair.Key).Append('=').Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (string gear in Gears) data.Append("gear.").Append(gear).Append("=1\n");
            foreach (string grant in Grants) data.Append("grant.").Append(grant).Append("=1\n");
            string tmp = path + ".new";
            File.WriteAllText(tmp, data.ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
            else File.Move(tmp, path);
        }
        internal static DongtianProgress Load(string path)
        {
            DongtianProgress value;
            if (TryLoad(path, out value)) return value;
            if (TryLoad(path + ".bak", out value)) { value.LastNotice = "已从备用洞天档案恢复"; return value; }
            return new DongtianProgress();
        }
        private static bool TryLoad(string path, out DongtianProgress p)
        {
            p = null;
            if (!File.Exists(path)) return false;
            try
            {
                DongtianProgress next = new DongtianProgress();
                int version = -1;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int equals = line.IndexOf('=');
                    if (equals < 1) continue;
                    string key = line.Substring(0, equals), value = line.Substring(equals + 1);
                    int n; long l;
                    if (key == "version" && Int32.TryParse(value, out n)) version = n;
                    else if (key == "level" && Int32.TryParse(value, out n) && n >= 1 && n <= MaxFields) next.Level = n;
                    else if (key == "furnace") { string r; long t; if (value == "" || ParseSlot(value, out r, out t)) next.Furnace = value == "" ? null : value; }
                    else if (key == "forge") { string r; long t; if (value == "" || ParseSlot(value, out r, out t)) next.Forge = value == "" ? null : value; }
                    else if (key == "importseq" && Int32.TryParse(value, out n) && n >= 0) next.ImportSeq = Math.Min(n, 1000000);
                    else if (key.StartsWith("field.", StringComparison.Ordinal))
                    {
                        int slot; string r; long t;
                        if (Int32.TryParse(key.Substring(6), out slot) && slot >= 0 && slot < MaxFields && ParseSlot(value, out r, out t)
                            && DongtianRecipes.Seed(r) != null) next.Fields[slot] = value;
                    }
                    else if (key.StartsWith("bag.", StringComparison.Ordinal) && Int32.TryParse(value, out n) && n > 0 && n < 1000000)
                    {
                        string id = key.Substring(4);
                        if (id.Length > 0 && id.Length <= 32 && id.IndexOfAny(new[] { '\r', '\n', '=', '|' }) < 0) next.Bag[id] = n;
                    }
                    else if (key.StartsWith("buff.", StringComparison.Ordinal) && Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out l) && l > 0)
                    {
                        string id = key.Substring(5);
                        if (id.Length > 0 && id.Length <= 32 && id.IndexOfAny(new[] { '\r', '\n', '=', '|' }) < 0) next.Buffs[id] = l;
                    }
                    else if (key.StartsWith("gear.", StringComparison.Ordinal) && value == "1" && DongtianRecipes.Gear(key.Substring(5)) != null) next.Gears.Add(key.Substring(5));
                    else if (key.StartsWith("grant.", StringComparison.Ordinal) && value == "1")
                    {
                        string id = key.Substring(6);
                        if (id.Length > 0 && id.Length <= 48 && id.IndexOfAny(new[] { '\r', '\n', '=', '|' }) < 0) next.Grants.Add(id);
                    }
                }
                if (version < 1 || version > FormatVersion) return false;
                p = next; return true;
            }
            catch { return false; }
        }

        // —— 材料入库双向对账（启动调用，幂等）——
        // dtin-<mat>-<seq>：洞天档与灵域档各记一份。
        // 灵域已记账洞天未记 → 灵域档写入成功而洞天档失败：回滚，材料返还灵域。
        // 洞天已记账灵域未记 → 洞天档成功灵域档失败：优先从灵域补扣；灵域库存
        // 已被他用则从洞天袋移除等量材料（绝不复制）。
        internal static bool ReconcileTransfers(DongtianProgress dongtian, LingyuProgress lingyu)
        {
            if (dongtian == null || lingyu == null) return false;
            bool changed = false;
            List<string> orphanLingyu = null;
            foreach (KeyValuePair<string, long> pair in lingyu.Grants)
                if (pair.Key.StartsWith("dtin-", StringComparison.Ordinal) && !dongtian.Grants.Contains(pair.Key))
                    (orphanLingyu ?? (orphanLingyu = new List<string>())).Add(pair.Key);
            if (orphanLingyu != null)
                foreach (string id in orphanLingyu)
                {
                    string mat; int count;
                    if (ParseTransferId(id, out mat, out count)) { int stock; lingyu.Materials.TryGetValue(mat, out stock); lingyu.Materials[mat] = stock + count; }
                    lingyu.Grants.Remove(id);
                    changed = true;
                }
            List<string> orphanDongtian = null;
            foreach (string id in dongtian.Grants)
                if (id.StartsWith("dtin-", StringComparison.Ordinal) && !lingyu.Grants.ContainsKey(id))
                    (orphanDongtian ?? (orphanDongtian = new List<string>())).Add(id);
            if (orphanDongtian != null)
                foreach (string id in orphanDongtian)
                {
                    string mat; int count;
                    if (ParseTransferId(id, out mat, out count))
                    {
                        int stock; lingyu.Materials.TryGetValue(mat, out stock);
                        if (stock >= count)
                        {
                            stock -= count;
                            if (stock <= 0) lingyu.Materials.Remove(mat); else lingyu.Materials[mat] = stock;
                        }
                        else
                        {
                            int bag = dongtian.CountOf(mat);
                            if (bag <= count) dongtian.Bag.Remove(mat); else dongtian.Bag[mat] = bag - count;
                        }
                    }
                    lingyu.Grants[id] = DateTime.UtcNow.Ticks;
                    changed = true;
                }
            return changed;
        }
        internal static bool ParseTransferId(string id, out string material, out int count)
        {
            material = ""; count = 0;
            if (id == null || !id.StartsWith("dtin-", StringComparison.Ordinal)) return false;
            string[] parts = id.Substring(5).Split('-');
            // dtin-<mat>-<seq>：mat 本身可能含连字符，序号取末段。
            if (parts.Length < 2) return false;
            int seq;
            if (!Int32.TryParse(parts[parts.Length - 1], out seq)) return false;
            material = String.Join("-", parts, 0, parts.Length - 1);
            count = 1;
            return material.Length > 0;
        }

        internal static string SelfTest()
        {
            int n = 0;
            Action<bool, string> check = delegate(bool okay, string label)
            {
                if (!okay) throw new InvalidOperationException("Dongtian: " + label);
                n++;
            };
            DateTime t0 = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
            check(DongtianRecipes.Seeds.Length == 3 && DongtianRecipes.Pills.Length == 4 && DongtianRecipes.Gears.Length == 3, "catalog shape");
            foreach (DongtianPillDef pill in DongtianRecipes.Pills)
                foreach (string mat in pill.Materials)
                    check(DongtianRecipes.Seed(mat) != null || mat == "shuiyue-lingsui", "every pill material has a source: " + pill.Id);
            // 灵田：时间制成熟，离线按真实流逝。
            DongtianProgress farm = new DongtianProgress();
            check(farm.UnlockedFields == 2 && !farm.Plant(2, "linggu", t0), "field slots are gated by the cave level");
            check(farm.Plant(0, "linggu", t0) && !farm.Plant(0, "linggu", t0), "an occupied field refuses replanting");
            check(!farm.Harvest(0, t0.AddHours(1)), "an unripe field cannot be harvested");
            check(farm.Harvest(0, t0.AddHours(2)) && farm.CountOf("linggu") == 2, "harvest yields into the bag after the real duration");
            // 丹炉：材料消耗 + 收丹即服 + 限时增益。
            check(!farm.StartPill("gushen", t0), "a pill refuses to start without its materials");
            check(farm.StartPill("juqi", t0) && farm.CountOf("linggu") == 0 && !farm.StartPill("gushen", t0), "brewing consumes the herbs and occupies the furnace");
            check(!farm.CollectPill(t0.AddMinutes(29)) && farm.CollectPill(t0.AddMinutes(30)) && farm.BuffActive("buff-juqi", t0.AddHours(1)), "the pill activates its buff on collection");
            check(Math.Abs(farm.QiRateFactor(t0.AddHours(1)) - 1.25) < 0.0001 && Math.Abs(farm.QiRateFactor(t0.AddHours(13)) - 1.0) < 0.0001, "the qi buff applies and expires");
            // 器灵：常驻、不堆叠。
            farm.Bag["jianxin"] = 1; farm.Bag["linggu"] = 2; // 剑丸需 剑心草×1 + 灵谷×2
            check(farm.StartGear("jianwan", t0) && farm.CollectGear(t0.AddHours(1)) && farm.HasGear("jianwan") && !farm.StartGear("jianwan", t0), "a forged gear is permanent and never stacks");
            check(Math.Abs(farm.DamageFactor(t0) - 1.05) < 0.0001, "gear damage factor applies");
            farm.Buffs.Clear(); farm.Gears.Clear();
            check(Math.Abs(farm.QiRateFactor(t0) - 1.0) < 0.0001 && Math.Abs(farm.ReviveTimeFactor(t0) - 1.0) < 0.0001 && Math.Abs(farm.FurnaceTimeFactor - 1.0) < 0.0001, "factors return to baseline without buffs or gears");
            // 存档往返 + 畸形行拒收。
            string fixture = Path.Combine(Path.GetTempPath(), "dongtian-selftest-" + Guid.NewGuid().ToString("N"));
            string file = Path.Combine(fixture, "dongtian-v1.txt");
            try
            {
                DongtianProgress saved = new DongtianProgress();
                saved.Level = 3; saved.Bag["linggu"] = 5; saved.Buffs["buff-juqi"] = t0.Ticks; saved.Gears.Add("putuan"); saved.ImportSeq = 7;
                saved.Plant(0, "ningshen", t0); saved.Grants.Add("dtin-shuiyue-lingsui-3");
                saved.Save(file);
                DongtianProgress back = DongtianProgress.Load(file);
                check(back.Level == 3 && back.CountOf("linggu") == 5 && back.Buffs["buff-juqi"] == t0.Ticks && back.HasGear("putuan")
                    && back.ImportSeq == 7 && back.Fields[0] != null && back.Grants.Contains("dtin-shuiyue-lingsui-3"), "archive round trip");
                File.WriteAllText(file, "version=1\nlevel=99\nfield.9=linggu,123\nfield.0=bad=recipe,123\nbag.x=-3\nbag.bad|id=2\nbuff.b=abc\ngear.unknown=1\ngrant.g=1\nimportseq=7\n", new UTF8Encoding(false));
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                DongtianProgress sanitized = DongtianProgress.Load(file);
                check(sanitized.Level == 1 && sanitized.Fields[0] == null && sanitized.Bag.Count == 0 && sanitized.Buffs.Count == 0
                    && sanitized.Gears.Count == 0 && sanitized.Grants.Count == 1 && sanitized.ImportSeq == 7, "malformed rows are dropped on load");
                File.WriteAllText(file, "version=99\nlevel=1\n", new UTF8Encoding(false));
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                check(DongtianProgress.Load(file).Level == 1, "an unknown future version falls back to a fresh cave");
                // 入库双向对账。
                string mat; int cnt;
                check(ParseTransferId("dtin-shuiyue-lingsui-12", out mat, out cnt) && mat == "shuiyue-lingsui" && cnt == 1 && !ParseTransferId("dtin-broken", out mat, out cnt), "transfer id parsing");
                // 灵域已扣已记账、洞天未记账 → 回滚返还灵域。
                DongtianProgress d1 = new DongtianProgress();
                LingyuProgress l1 = new LingyuProgress();
                l1.Grants["dtin-shuiyue-lingsui-1"] = DateTime.UtcNow.Ticks;
                check(ReconcileTransfers(d1, l1) && l1.Materials["shuiyue-lingsui"] == 1 && !l1.Grants.ContainsKey("dtin-shuiyue-lingsui-1"), "lingyu-side orphan rolls the material back");
                // 洞天已收已记账、灵域未记账且库存够 → 补扣灵域。
                DongtianProgress d2 = new DongtianProgress(); d2.Bag["shuiyue-lingsui"] = 1; d2.Grants.Add("dtin-shuiyue-lingsui-2");
                LingyuProgress l2 = new LingyuProgress(); l2.Materials["shuiyue-lingsui"] = 2;
                check(ReconcileTransfers(d2, l2) && l2.Materials["shuiyue-lingsui"] == 1 && l2.Grants.ContainsKey("dtin-shuiyue-lingsui-2") && d2.CountOf("shuiyue-lingsui") == 1, "dongtian-side orphan settles the lingyu debit");
                // 灵域库存已被他用 → 从洞天袋移除，绝不复制。
                DongtianProgress d3 = new DongtianProgress(); d3.Bag["shuiyue-lingsui"] = 1; d3.Grants.Add("dtin-shuiyue-lingsui-3");
                LingyuProgress l3 = new LingyuProgress();
                check(ReconcileTransfers(d3, l3) && d3.CountOf("shuiyue-lingsui") == 0 && l3.Grants.ContainsKey("dtin-shuiyue-lingsui-3"), "a spent lingyu stock removes the duplicate from the bag instead");
                check(!ReconcileTransfers(d3, l3), "transfer reconcile is idempotent");
            }
            finally { try { if (Directory.Exists(fixture)) Directory.Delete(fixture, true); } catch { } }
            return "Dongtian: " + n + " checks passed (fields, alchemy, forge, buffs, archive fixture, two-way transfer reconcile).";
        }
    }
}
