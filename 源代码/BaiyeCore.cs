using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace NetBunny
{
    // 百业证道：现实中的工作、学习、创作与娱乐，化为游戏里的后天道途。
    // 本模块是纯逻辑：不依赖 WinForms，不触碰战斗、修仙主进度与真实系统维护；
    // 唯一的文件读写是自己独立的、带版本号的 baiye-v1.txt 存档。
    //
    // 隐私契约（写死在识别器里，不接受配置绕过）：
    // - 只读取前台窗口所属进程的「进程名」，用于判断应用类别；
    // - 永不读取窗口标题、文档正文、代码内容、聊天内容、网页历史与屏幕画面；
    // - 活动分类与结算数据只保存在本机存档中。
    internal sealed class BaiyePathDef
    {
        internal readonly string Id, Name, Description;
        internal BaiyePathDef(string id, string name, string description) { Id = id; Name = name; Description = description; }
    }

    // 一条自动识别规则：进程名（小写，不含 .exe）命中即归入对应道途。
    // 名称以 * 结尾时按前缀匹配（用于版本号变化的进程，如 vegas*）。
    internal sealed class BaiyeAppRule
    {
        internal readonly string PathId, ActivityLabel;
        internal readonly string[] ProcessNames;
        internal BaiyeAppRule(string pathId, string activityLabel, params string[] processNames)
        { PathId = pathId; ActivityLabel = activityLabel; ProcessNames = processNames; }
        internal bool Matches(string processNameLower)
        {
            for (int i = 0; i < ProcessNames.Length; i++)
            {
                string name = ProcessNames[i];
                if (name.EndsWith("*", StringComparison.Ordinal))
                { if (processNameLower.StartsWith(name.Substring(0, name.Length - 1), StringComparison.Ordinal)) return true; }
                else if (processNameLower == name) return true;
            }
            return false;
        }
    }

    // 玩家手动标记的具体活动（用于识别不可靠时与游历道的主动选择）。
    internal sealed class BaiyeManualMark
    {
        internal readonly string Id, PathId, Label;
        internal BaiyeManualMark(string id, string pathId, string label) { Id = id; PathId = pathId; Label = label; }
    }

    // 所有百业数值与映射都在这个目录里，UI 代码不散落任何收益数字。
    internal static class BaiyeCatalog
    {
        internal const double DaoYunPerActiveSecond = 0.10;          // 每分钟有效活动约 6 道韵
        internal const double MaxCreditSecondsPerTick = 3.0;         // 卡死的界面不能伪造活动时长
        internal const double IdleLimitSeconds = 90;                 // 超过即视为无人操作/锁屏，停止主动结算
        internal const double LevelBaseYun = 60;                     // 等级 L 需要累计 60·L·(L+1)/2 道韵
        internal const int GainLogCapacity = 40;

        internal static readonly BaiyePathDef[] Paths = {
            new BaiyePathDef("huajing",   "画境道", "以演示与画面构图入道"),
            new BaiyePathDef("wen",       "文道",   "以文字创作入道"),
            new BaiyePathDef("youli",     "游历道", "以网上游历见闻入道"),
            new BaiyePathDef("zhen",      "阵道",   "以编程构筑阵法入道"),
            new BaiyePathDef("tianji",    "天机道", "以 AI 协力推演天机入道"),
            new BaiyePathDef("qi",        "器道",   "以建模与工程造物入道"),
            new BaiyePathDef("guangying", "光影道", "以影像剪辑叙事入道"),
            new BaiyePathDef("zhan",      "战道",   "以电子游戏征伐入道")
        };
        internal static BaiyePathDef FindPath(string id)
        {
            for (int i = 0; i < Paths.Length; i++) if (Paths[i].Id == id) return Paths[i];
            return null;
        }
        internal static bool IsValidPath(string id) { return FindPath(id) != null; }

        internal static readonly string[] LevelNames = { "未入门", "初窥门径", "小有所成", "融会贯通", "炉火纯青", "登峰造极" };
        internal static string LevelName(int level)
        {
            if (level <= 0) return LevelNames[0];
            return level < LevelNames.Length ? LevelNames[level] : LevelNames[LevelNames.Length - 1] + " · " + level.ToString(CultureInfo.InvariantCulture) + "阶";
        }
        internal static double YunForLevel(int level)
        {
            if (level <= 0) return 0;
            return LevelBaseYun * level * (level + 1) / 2.0;
        }
        internal static int LevelForYun(double yun)
        {
            if (Double.IsNaN(yun) || yun < YunForLevel(1)) return 0;
            int level = 0;
            while (yun >= YunForLevel(level + 1)) level++;
            return level;
        }

        // 前台应用类别 → 道途。只凭进程名粗分类，拿不准的交给手动标记。
        internal static readonly BaiyeAppRule[] AppRules = {
            new BaiyeAppRule("huajing", "演示文稿制作", "powerpnt", "wpp"),
            new BaiyeAppRule("wen", "文字创作", "winword", "wps", "notepad", "typora", "obsidian", "marktext"),
            new BaiyeAppRule("youli", "网上游历", "chrome", "msedge", "firefox", "iexplore", "opera", "brave", "vivaldi", "arc", "360chrome", "360se", "qqbrowser", "sogouexplorer"),
            new BaiyeAppRule("zhen", "编程开发", "devenv", "code", "vscodium", "cursor", "windsurf", "trae", "rider", "idea64", "pycharm64", "webstorm64", "clion64", "goland64", "rustrover64", "androidstudio", "eclipse", "sublime_text", "windowsterminal"),
            new BaiyeAppRule("tianji", "AI 助手协作", "chatgpt", "claude", "kimi", "doubao", "lmstudio", "jan", "ollama app"),
            new BaiyeAppRule("qi", "建模与工程设计", "blender", "3dsmax", "maya", "cinema4d", "zbrush", "acad", "sldworks", "fusion360", "rhino", "sketchup", "inventor", "freecad", "houdini"),
            new BaiyeAppRule("guangying", "影像剪辑创作", "adobe premiere pro", "resolve", "afterfx", "jianyingpro", "capcut", "bcut", "obs64", "vegas*"),
            new BaiyeAppRule("zhan", "游戏娱乐", "steam", "epicgameslauncher", "eadesktop", "battlenet", "wegame", "leagueclient", "league of legends", "dota2", "cs2", "csgo", "gta5", "eldenring", "cyberpunk2077")
        };
        // 返回识别结果；无法可靠判断时返回 null（由玩家手动标记兜底）。
        internal static BaiyeAppRule Classify(string processName)
        {
            if (String.IsNullOrEmpty(processName)) return null;
            string lower = processName.Trim().ToLowerInvariant();
            if (lower.EndsWith(".exe", StringComparison.Ordinal)) lower = lower.Substring(0, lower.Length - 4);
            if (lower.Length == 0) return null;
            for (int i = 0; i < AppRules.Length; i++) if (AppRules[i].Matches(lower)) return AppRules[i];
            return null;
        }

        internal static readonly BaiyeManualMark[] ManualMarks = {
            new BaiyeManualMark("manual-youli-read",   "youli",     "网上查阅资料"),
            new BaiyeManualMark("manual-youli-learn",  "youli",     "在线课程学习"),
            new BaiyeManualMark("manual-youli-media",  "youli",     "影音娱乐"),
            new BaiyeManualMark("manual-youli-social", "youli",     "社区交流"),
            new BaiyeManualMark("manual-huajing",      "huajing",   "演示文稿制作"),
            new BaiyeManualMark("manual-wen",          "wen",       "文字创作"),
            new BaiyeManualMark("manual-zhen",         "zhen",      "编程开发"),
            new BaiyeManualMark("manual-tianji",       "tianji",    "AI 助手协作"),
            new BaiyeManualMark("manual-qi",           "qi",        "建模与工程设计"),
            new BaiyeManualMark("manual-guangying",    "guangying", "影像剪辑创作"),
            new BaiyeManualMark("manual-zhan",         "zhan",      "游戏娱乐")
        };
        internal static BaiyeManualMark FindManualMark(string id)
        {
            for (int i = 0; i < ManualMarks.Length; i++) if (ManualMarks[i].Id == id) return ManualMarks[i];
            return null;
        }
    }

    // 一条道韵获取记录：只记道途、数量与来源类别，永不记录具体应用名或内容。
    internal sealed class BaiyeGainEntry
    {
        internal long UtcTicks;
        internal string PathId = "";
        internal double Amount;
        internal string Source = "";
        internal string Format()
        {
            BaiyePathDef path = BaiyeCatalog.FindPath(PathId);
            DateTime local = new DateTime(UtcTicks, DateTimeKind.Utc).ToLocalTime();
            string when = local.Date == DateTime.Today ? local.ToString("HH:mm", CultureInfo.InvariantCulture) : local.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
            return when + "　" + (path == null ? "未知道途" : path.Name) + " +" + Amount.ToString("0.#", CultureInfo.InvariantCulture) + " 道韵 · " + Source;
        }
    }

    // 机缘事件定义：多道共鸣的第一版事件结构。
    // 预留字段 Kind / Payload 供后续连续机缘、特殊 NPC 与软件秘境扩展，
    // 不需要改动存档格式（未知机缘在旧版本读取时被忽略，存档向前兼容）。
    internal sealed class BaiyeEncounterDef
    {
        internal string Id, Name, Hint, RewardText, HeritageId, HeritageName;
        internal string Kind = "resonance";   // resonance | npc | realm（预留）
        internal string Payload = "";          // 后续机缘链与秘境参数（预留）
        internal string[] RequiredPaths = new string[0];
        internal double[] RequiredYun = new double[0];
        internal long QiBonus;
        internal bool IsSatisfiedBy(BaiyeProgress progress)
        {
            for (int i = 0; i < RequiredPaths.Length; i++)
                if (progress.YunOf(RequiredPaths[i]) < RequiredYun[i]) return false;
            return true;
        }
    }

    internal static class BaiyeEncounters
    {
        // 第一条多道共鸣：阵道 + 天机道 → 天机演算。
        // 道韵只在前台对应活动结算，且同一时间窗口只结算一种活动，
        // 因此两门同时达标必然意味着玩家在不同时间分别编程与使用 AI。
        internal static readonly BaiyeEncounterDef[] All = {
            new BaiyeEncounterDef
            {
                Id = "tianji-yansuan", Name = "天机演算",
                Hint = "阵道与天机道皆有精进（各 120 道韵）时，可窥天机演算之妙",
                RewardText = "阵法与天机两道交相印证，悟得阵图「天机阵图」",
                HeritageId = "tianji-chart", HeritageName = "天机阵图",
                RequiredPaths = new[] { "zhen", "tianji" }, RequiredYun = new[] { 120.0, 120.0 },
                QiBonus = 120
            }
        };
        // 返回本次新解锁的机缘；已解锁的不会重复触发。解锁会写入存档字段并登记传承。
        internal static List<BaiyeEncounterDef> Evaluate(BaiyeProgress progress, DateTime utc)
        {
            List<BaiyeEncounterDef> unlocked = new List<BaiyeEncounterDef>();
            if (progress == null) return unlocked;
            for (int i = 0; i < All.Length; i++)
            {
                BaiyeEncounterDef def = All[i];
                if (progress.EncounterUnlockedUtc.ContainsKey(def.Id)) continue;
                if (!def.IsSatisfiedBy(progress)) continue;
                progress.EncounterUnlockedUtc[def.Id] = utc.Ticks;
                if (!String.IsNullOrEmpty(def.HeritageId) && !progress.Heritages.Contains(def.HeritageId))
                    progress.Heritages.Add(def.HeritageId);
                unlocked.Add(def);
            }
            return unlocked;
        }
    }

    // 机缘奖励的跨存档幂等结算。协议：
    //   1) 机缘解锁先持久化到百业存档（enc.<id>）；
    //   2) 修为入账并把 grant.<id> 标记写进修仙存档；
    //   3) 百业存档补 grant.<id> 完成标记。
    // 任一窗口崩溃/断电，重启后用两份存档对账：
    // 修仙侧已有标记 → 只补百业侧标记（不重复发放）；
    // 两侧都无标记   → 重新发放（不永久丢失）。
    internal static class BaiyeSettlement
    {
        internal static BaiyeEncounterDef FindEncounter(string id)
        {
            for (int i = 0; i < BaiyeEncounters.All.Length; i++) if (BaiyeEncounters.All[i].Id == id) return BaiyeEncounters.All[i];
            return null;
        }
        // 修仙存档侧已有发放标记的机缘：补齐百业侧完成标记（含已下线机缘的封存）。
        internal static bool SyncGranted(BaiyeProgress baiye, ISet<string> cultivationGrants, DateTime utc)
        {
            bool changed = false;
            foreach (KeyValuePair<string, long> pair in baiye.EncounterUnlockedUtc)
            {
                if (baiye.RewardGrantedUtc.ContainsKey(pair.Key)) continue;
                if (cultivationGrants.Contains(pair.Key) || FindEncounter(pair.Key) == null)
                { baiye.RewardGrantedUtc[pair.Key] = utc.Ticks; changed = true; }
            }
            return changed;
        }
        // 需要发放的机缘：已解锁、两侧都没有完成标记、定义仍在线且有修为奖励。
        internal static List<BaiyeEncounterDef> PendingRewards(BaiyeProgress baiye, ISet<string> cultivationGrants)
        {
            List<BaiyeEncounterDef> pending = new List<BaiyeEncounterDef>();
            foreach (KeyValuePair<string, long> pair in baiye.EncounterUnlockedUtc)
            {
                if (baiye.RewardGrantedUtc.ContainsKey(pair.Key) || cultivationGrants.Contains(pair.Key)) continue;
                BaiyeEncounterDef def = FindEncounter(pair.Key);
                if (def != null && def.QiBonus > 0) pending.Add(def);
            }
            return pending;
        }
    }

    // 百业存档：独立文件 baiye-v1.txt，损坏时从 .bak 恢复，绝不覆盖修仙与战斗存档。
    internal sealed class BaiyeProgress
    {
        internal const int FormatVersion = 1;
        // 识别模式：0 关闭；1 仅识别应用类别；2 应用类别 + 手动活动标记。
        internal int Mode;
        // 首次开启前必须完成的功能说明；未确认前无法启用任何识别模式。
        internal bool FirstRunExplained;
        internal string ManualMarkId = "";
        internal readonly Dictionary<string, double> Yun = new Dictionary<string, double>(StringComparer.Ordinal);
        internal readonly Dictionary<string, long> LastActiveUtcTicks = new Dictionary<string, long>(StringComparer.Ordinal);
        internal readonly List<BaiyeGainEntry> Log = new List<BaiyeGainEntry>();
        internal readonly Dictionary<string, long> EncounterUnlockedUtc = new Dictionary<string, long>(StringComparer.Ordinal);
        // 机缘奖励发放台账：enc.<id> 记录解锁，grant.<id> 记录奖励已确实写入修仙存档。
        // 两者分离 + 修仙存档侧的 ExternalGrants 标记，构成跨存档幂等结算。
        internal readonly Dictionary<string, long> RewardGrantedUtc = new Dictionary<string, long>(StringComparer.Ordinal);
        internal readonly List<string> Heritages = new List<string>();

        internal double YunOf(string pathId)
        {
            double value; return Yun.TryGetValue(pathId, out value) ? value : 0;
        }
        internal int LevelOf(string pathId) { return BaiyeCatalog.LevelForYun(YunOf(pathId)); }
        internal bool Discovered(string pathId) { return YunOf(pathId) > 0; }
        // 返回是否升入新等级（用于即时存档与提示）。
        internal bool AddYun(string pathId, double amount, DateTime utc)
        {
            if (!BaiyeCatalog.IsValidPath(pathId) || Double.IsNaN(amount) || Double.IsInfinity(amount) || amount <= 0) return false;
            int before = LevelOf(pathId);
            Yun[pathId] = YunOf(pathId) + amount;
            LastActiveUtcTicks[pathId] = utc.Ticks;
            return LevelOf(pathId) > before;
        }
        internal void AddLog(string pathId, double amount, string source, DateTime utc)
        {
            if (!BaiyeCatalog.IsValidPath(pathId) || amount <= 0) return;
            Log.Add(new BaiyeGainEntry { UtcTicks = utc.Ticks, PathId = pathId, Amount = amount, Source = source });
            while (Log.Count > BaiyeCatalog.GainLogCapacity) Log.RemoveAt(0);
        }
        // 启动时对齐：崩溃丢失的片段会让道韵总量超过记录合计，
        // 补一条「历史结算补记」，保证每份道韵都有合理的获取记录。
        internal void ReconcileLog(DateTime utc)
        {
            Dictionary<string, double> logged = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (BaiyeGainEntry entry in Log)
                logged[entry.PathId] = (logged.ContainsKey(entry.PathId) ? logged[entry.PathId] : 0) + entry.Amount;
            foreach (KeyValuePair<string, double> pair in Yun)
            {
                double recorded = logged.ContainsKey(pair.Key) ? logged[pair.Key] : 0;
                double diff = pair.Value - recorded;
                if (diff >= 1) AddLog(pair.Key, Math.Round(diff, 1), "历史结算补记", utc);
            }
        }

        internal static BaiyeProgress Load(string path)
        {
            BaiyeProgress value;
            if (TryLoad(path, out value)) return value;
            if (TryLoad(path + ".bak", out value)) return value;
            return new BaiyeProgress();
        }
        private static bool TryLoad(string path, out BaiyeProgress progress)
        {
            progress = null;
            if (!File.Exists(path)) return false;
            try
            {
                BaiyeProgress next = new BaiyeProgress();
                int version = -1;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int equals = line.IndexOf('=');
                    if (equals < 1) continue;
                    string key = line.Substring(0, equals), value = line.Substring(equals + 1);
                    int n; double d; long l;
                    if (key == "version" && Int32.TryParse(value, out n)) version = n;
                    else if (key == "mode" && Int32.TryParse(value, out n)) next.Mode = n;
                    else if (key == "explained") next.FirstRunExplained = value == "1";
                    else if (key == "manual") next.ManualMarkId = BaiyeCatalog.FindManualMark(value) == null ? "" : value;
                    else if (key.StartsWith("yun.", StringComparison.Ordinal) && Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    {
                        string id = key.Substring(4);
                        if (BaiyeCatalog.IsValidPath(id) && !Double.IsInfinity(d) && d >= 0) next.Yun[id] = d;
                    }
                    else if (key.StartsWith("last.", StringComparison.Ordinal) && Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out l))
                    {
                        string id = key.Substring(5);
                        if (BaiyeCatalog.IsValidPath(id) && l >= 0) next.LastActiveUtcTicks[id] = l;
                    }
                    else if (key.StartsWith("enc.", StringComparison.Ordinal) && Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out l))
                    {
                        if (l >= 0) next.EncounterUnlockedUtc[key.Substring(4)] = l;
                    }
                    else if (key.StartsWith("grant.", StringComparison.Ordinal) && Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out l))
                    {
                        string grantId = key.Substring(6);
                        if (l >= 0 && grantId.Length > 0 && grantId.Length <= 40 && grantId.IndexOfAny(new[] { '\r', '\n', '=', '|' }) < 0)
                            next.RewardGrantedUtc[grantId] = l;
                    }
                    else if (key == "heritage" && value.Length > 0 && value.Length <= 40 && !next.Heritages.Contains(value)) next.Heritages.Add(value);
                    else if (key == "log")
                    {
                        string[] parts = value.Split('|');
                        double amount; long ticks;
                        if (parts.Length == 4 && Int64.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks) &&
                            Double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out amount) &&
                            BaiyeCatalog.IsValidPath(parts[1]) && amount > 0 && ticks >= 0 &&
                            parts[3].IndexOfAny(new[] { '\r', '\n', '|', '=' }) < 0)
                            next.Log.Add(new BaiyeGainEntry { UtcTicks = ticks, PathId = parts[1], Amount = amount, Source = parts[3] });
                    }
                }
                if (version != FormatVersion || next.Mode < 0 || next.Mode > 2) return false;
                while (next.Log.Count > BaiyeCatalog.GainLogCapacity) next.Log.RemoveAt(0);
                progress = next; return true;
            }
            catch { return false; }
        }
        internal void Save(string path)
        {
            string folder = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            string tmp = path + ".new";
            StringBuilder data = new StringBuilder();
            data.Append("version=").Append(FormatVersion).Append('\n');
            data.Append("mode=").Append(Mode).Append('\n');
            data.Append("explained=").Append(FirstRunExplained ? "1" : "0").Append('\n');
            data.Append("manual=").Append(ManualMarkId).Append('\n');
            foreach (KeyValuePair<string, double> pair in Yun)
                data.Append("yun.").Append(pair.Key).Append('=').Append(pair.Value.ToString("0.##", CultureInfo.InvariantCulture)).Append('\n');
            foreach (KeyValuePair<string, long> pair in LastActiveUtcTicks)
                data.Append("last.").Append(pair.Key).Append('=').Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (KeyValuePair<string, long> pair in EncounterUnlockedUtc)
                data.Append("enc.").Append(pair.Key).Append('=').Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (KeyValuePair<string, long> pair in RewardGrantedUtc)
                data.Append("grant.").Append(pair.Key).Append('=').Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (string heritage in Heritages) data.Append("heritage=").Append(heritage).Append('\n');
            foreach (BaiyeGainEntry entry in Log)
                data.Append("log=").Append(entry.UtcTicks.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(entry.PathId).Append('|').Append(entry.Amount.ToString("0.#", CultureInfo.InvariantCulture))
                    .Append('|').Append(entry.Source).Append('\n');
            File.WriteAllText(tmp, data.ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
            else File.Move(tmp, path);
        }

        internal static string SelfTest()
        {
            int n = 0;
            Action<bool, string> check = delegate(bool okay, string label)
            {
                if (!okay) throw new InvalidOperationException("Baiye: " + label);
                n++;
            };
            // 目录与识别：八条道途齐备，映射正确，未知进程返回 null。
            check(BaiyeCatalog.Paths.Length == 8, "eight dao paths");
            check(BaiyeCatalog.Classify("POWERPNT").PathId == "huajing", "PowerPoint belongs to the presentation dao");
            check(BaiyeCatalog.Classify("chrome.exe").PathId == "youli", "browser belongs to wandering dao");
            check(BaiyeCatalog.Classify("devenv").PathId == "zhen", "IDE belongs to formation dao");
            check(BaiyeCatalog.Classify("kimi").PathId == "tianji", "AI tool belongs to tianji dao");
            check(BaiyeCatalog.Classify("blender").PathId == "qi", "modeler belongs to artifact dao");
            check(BaiyeCatalog.Classify("vegas160").PathId == "guangying", "versioned editor matches prefix rule");
            check(BaiyeCatalog.Classify("steam").PathId == "zhan", "game platform belongs to battle dao");
            check(BaiyeCatalog.Classify("some-unknown-app") == null && BaiyeCatalog.Classify("") == null, "unreliable recognition returns null for manual marking");
            // 等级曲线：阈值单调，0/60/180 分别对应 0/1/2 级。
            check(BaiyeCatalog.LevelForYun(0) == 0 && BaiyeCatalog.LevelForYun(59.9) == 0, "below first threshold stays unranked");
            check(BaiyeCatalog.LevelForYun(60) == 1 && BaiyeCatalog.LevelForYun(180) == 2, "triangular level thresholds");
            check(BaiyeCatalog.YunForLevel(4) > BaiyeCatalog.YunForLevel(3), "level costs increase monotonically");
            // 结算：有效活动积累道韵，跨级时报告升段。
            BaiyeProgress progress = new BaiyeProgress();
            check(!progress.AddYun("zhen", 30, DateTime.UtcNow), "no level-up below threshold");
            check(progress.AddYun("zhen", 35, DateTime.UtcNow) && progress.LevelOf("zhen") == 1, "crossing threshold reports a level-up");
            check(Math.Abs(progress.YunOf("zhen") - 65) < 0.001 && progress.Discovered("zhen"), "fractional dao-yun accumulates");
            check(!progress.AddYun("nonsense", 10, DateTime.UtcNow) && !progress.AddYun("wen", -5, DateTime.UtcNow), "invalid gains rejected");
            // 机缘：双道未达标不触发，达标触发一次并登记传承。
            BaiyeProgress resonance = new BaiyeProgress();
            check(BaiyeEncounters.Evaluate(resonance, DateTime.UtcNow).Count == 0, "resonance stays locked below thresholds");
            resonance.AddYun("zhen", 130, DateTime.UtcNow);
            check(BaiyeEncounters.Evaluate(resonance, DateTime.UtcNow).Count == 0, "a single strong dao cannot resonate alone");
            resonance.AddYun("tianji", 125, DateTime.UtcNow);
            List<BaiyeEncounterDef> unlocked = BaiyeEncounters.Evaluate(resonance, DateTime.UtcNow);
            check(unlocked.Count == 1 && unlocked[0].Id == "tianji-yansuan", "zhen + tianji unlock the first resonance");
            check(resonance.Heritages.Contains("tianji-chart"), "heritage recorded once unlocked");
            check(BaiyeEncounters.Evaluate(resonance, DateTime.UtcNow).Count == 0, "an unlocked encounter never refires");
            // 存档：往返持久化、容量上限、损坏回退、未知版本安全失败。
            string folder = Path.Combine(Path.GetTempPath(), "baiye-v6-selftest-" + Guid.NewGuid().ToString("N"));
            string file = Path.Combine(folder, "baiye-v1.txt");
            try
            {
                resonance.Mode = 2; resonance.FirstRunExplained = true; resonance.ManualMarkId = "manual-zhen";
                resonance.AddLog("zhen", 6.5, "自动识别", new DateTime(2026, 10, 17, 1, 2, 3, DateTimeKind.Utc));
                resonance.Save(file);
                BaiyeProgress loaded = Load(file);
                check(loaded.Mode == 2 && loaded.FirstRunExplained && loaded.ManualMarkId == "manual-zhen", "mode, explanation and manual mark round trip");
                check(Math.Abs(loaded.YunOf("zhen") - 130) < 0.01 && loaded.YunOf("tianji") > 119, "dao-yun survives save/load");
                check(loaded.EncounterUnlockedUtc.ContainsKey("tianji-yansuan") && loaded.Heritages.Contains("tianji-chart"), "encounter state survives save/load");
                check(loaded.Log.Count == 1 && loaded.Log[0].Source == "自动识别", "gain log survives save/load");
                resonance.Save(file); // second save creates the rolling .bak used below
                File.WriteAllText(file, "garbage-without-version", new UTF8Encoding(false));
                check(Load(file).YunOf("zhen") > 129, "corrupt main archive falls back to backup");
                File.WriteAllText(file, "version=99\nmode=2\n", new UTF8Encoding(false));
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                check(Load(file).Mode == 0 && Load(file).Yun.Count == 0, "unknown future version starts fresh instead of crashing");
                File.WriteAllText(file, "version=1\nmode=2\nmanual=bogus-mark\nyun.zhen=-50\nyun.fake=10\n", new UTF8Encoding(false));
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                BaiyeProgress sanitized = Load(file);
                check(sanitized.ManualMarkId == "" && sanitized.Yun.Count == 0, "bogus marks and invalid gains are dropped on load");
                File.WriteAllText(file, "version=1\nmode=7\nexplained=1\n", new UTF8Encoding(false));
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                check(Load(file).Mode == 0, "an out-of-range recognition mode falls back to a fresh archive");
                File.WriteAllText(file, "version=1\nmode=2\n", new UTF8Encoding(false));
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                BaiyeProgress unexplainedLoad = Load(file);
                check(unexplainedLoad.Mode == 2 && !unexplainedLoad.FirstRunExplained,
                    "a crafted archive may claim a mode but the missing acknowledgement survives as false");
                BaiyeProgress spam = new BaiyeProgress();
                for (int i = 0; i < 60; i++) spam.AddLog("wen", 1, "自动识别", DateTime.UtcNow);
                check(spam.Log.Count == BaiyeCatalog.GainLogCapacity, "gain log is bounded");
                // 跨存档幂等结算：异常退出/断电不重复发放，也不永久丢失。
                string cultFile = Path.Combine(folder, "cultivation-v2.txt");
                BaiyeProgress baiyeA = new BaiyeProgress { Mode = 1, FirstRunExplained = true };
                baiyeA.AddYun("zhen", 130, DateTime.UtcNow); baiyeA.AddYun("tianji", 130, DateTime.UtcNow);
                check(BaiyeEncounters.Evaluate(baiyeA, DateTime.UtcNow).Count == 1, "setup: resonance unlocked");
                // 窗口 a：解锁已落百业存档，进程在修为入账前崩溃 → 重启后补发且仅一次。
                baiyeA.Save(file);
                CultivationProgress cultA = new CultivationProgress(); cultA.EnsureIdentity(new Random(2)); cultA.Save(cultFile);
                BaiyeProgress reloadB = Load(file); CultivationProgress reloadC = CultivationProgress.Load(cultFile);
                check(!BaiyeSettlement.SyncGranted(reloadB, reloadC.ExternalGrants, DateTime.UtcNow), "fresh crash window has nothing to sync");
                List<BaiyeEncounterDef> pending = BaiyeSettlement.PendingRewards(reloadB, reloadC.ExternalGrants);
                check(pending.Count == 1 && pending[0].Id == "tianji-yansuan", "unpaid unlocked encounter is pending after restart");
                reloadC.AddQi(pending[0].QiBonus); reloadC.ExternalGrants.Add(pending[0].Id); reloadC.Save(cultFile);
                reloadB.RewardGrantedUtc[pending[0].Id] = DateTime.UtcNow.Ticks; reloadB.Save(file);
                BaiyeProgress againB = Load(file); CultivationProgress againC = CultivationProgress.Load(cultFile);
                check(BaiyeSettlement.PendingRewards(againB, againC.ExternalGrants).Count == 0, "a paid encounter never pays twice across restarts");
                check(againC.LifetimeQi == 120 && againC.ExternalGrants.Contains("tianji-yansuan"), "exactly one payout is durable in the cultivation archive");
                // 窗口 b：修仙侧标记已落地、百业侧完成标记丢失 → 只补标记，不重发。
                File.WriteAllText(file, "version=1\nmode=1\nexplained=1\nenc.tianji-yansuan=100\n", new UTF8Encoding(false));
                if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                BaiyeProgress desyncB = Load(file); CultivationProgress desyncC = CultivationProgress.Load(cultFile);
                check(BaiyeSettlement.PendingRewards(desyncB, desyncC.ExternalGrants).Count == 0, "cultivation-side marker blocks any double payout");
                check(BaiyeSettlement.SyncGranted(desyncB, desyncC.ExternalGrants, DateTime.UtcNow) && desyncB.RewardGrantedUtc.ContainsKey("tianji-yansuan"),
                    "the missing baiye-side marker is rebuilt from the cultivation side");
            }
            finally
            {
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
            }
            return "Baiye: " + n + " checks passed (catalog, settlement, resonance, isolated archive fixture).";
        }
    }

    // 活动识别与道韵结算器。三个数据源全部可注入：
    // 生产环境接系统探针，测试用合成数据，绝不直接读系统状态。
    internal sealed class BaiyeTracker
    {
        private readonly BaiyeProgress progress;
        internal Func<string> ForegroundProcessName = BaiyeSystemProbe.GetForegroundProcessName;
        internal Func<double> IdleSeconds = BaiyeSystemProbe.GetIdleSeconds;
        internal Func<DateTime> UtcNow = delegate { return DateTime.UtcNow; };
        // 前台探测按固定节奏采样（默认 2 秒），绝不跟随绘制帧率；
        // 两次探测之间沿用缓存状态结算，活动时长不受影响。
        internal double ProbeIntervalSeconds = 2.0;
        internal int ProbeCount; // 诊断/测试用：探测实际执行次数
        private double carry;
        private double probeCountdown;
        private bool probeIdle;
        private string probePathId, probeLabel, probeSource;
        private string segmentPath, segmentSource;
        private double segmentYun;
        private DateTime? manualSince;
        private string status = "百业识别未开启";

        internal BaiyeTracker(BaiyeProgress progress)
        {
            if (progress == null) throw new ArgumentNullException("progress");
            this.progress = progress;
        }
        internal string StatusText { get { return status; } }
        internal string CurrentPathId { get { return segmentPath; } }

        // 关闭识别或改动手动标记时调用：立即结算并丢弃当前片段，
        // 关闭后不会再有新的活动采样。
        internal void Reset()
        {
            FlushSegment(UtcNow());
            probeCountdown = 0; probePathId = null; probeLabel = null; probeSource = null; probeIdle = false; manualSince = null;
            status = progress.Mode == 0 ? "百业识别已关闭" : "等待识别前台活动";
        }
        // 正常退出前封口：把尚未落记录的活动片段写进获取记录，
        // 保证重启后每份道韵都有对应记录。
        internal void Seal() { FlushSegment(UtcNow()); }
        private void FlushSegment(DateTime utc)
        {
            if (segmentPath != null && segmentYun > 0) progress.AddLog(segmentPath, Math.Round(segmentYun, 1), segmentSource, utc);
            segmentPath = null; segmentSource = null; segmentYun = 0;
        }
        // 唯一接触系统的位置。任何一步异常都按「不可结算」处理。
        private void Probe()
        {
            ProbeCount++;
            double idle = IdleSeconds();
            if (Double.IsNaN(idle) || idle >= BaiyeCatalog.IdleLimitSeconds)
            {
                probeIdle = true; probePathId = null; probeLabel = null; probeSource = null;
                return;
            }
            probeIdle = false;
            BaiyeManualMark mark = progress.Mode == 2 ? BaiyeCatalog.FindManualMark(progress.ManualMarkId) : null;
            if (mark != null) { probePathId = mark.PathId; probeLabel = mark.Label; probeSource = "手动标记"; return; }
            BaiyeAppRule rule = BaiyeCatalog.Classify(ForegroundProcessName());
            if (rule == null) { probePathId = null; probeLabel = null; probeSource = null; return; }
            probePathId = rule.PathId; probeLabel = rule.ActivityLabel; probeSource = "自动识别";
        }

        // 每个控制器节拍调用一次，传入真实流逝秒数。
        // 返回升段提示文本（无变化为 null）；out leveledUp 用于即时存档。
        internal string Tick(double elapsedSeconds, out bool leveledUp)
        {
            leveledUp = false;
            // 底层隐私约束：未完成首次功能说明确认时，无论存档里是什么模式，
            // 都绝不读取前台应用信息、不发放道韵。
            if (!progress.FirstRunExplained) { status = "待确认百业功能说明"; return null; }
            if (progress.Mode == 0) { FlushSegment(UtcNow()); status = "百业识别已关闭"; return null; }
            if (Double.IsNaN(elapsedSeconds) || Double.IsInfinity(elapsedSeconds) || elapsedSeconds <= 0) return null;
            double elapsed = Math.Min(BaiyeCatalog.MaxCreditSecondsPerTick, elapsedSeconds);
            DateTime utc = UtcNow();
            // 按节奏探测前台状态；间隔期内沿用缓存，结算时长不受影响。
            probeCountdown -= elapsedSeconds;
            if (probeCountdown <= 0) { probeCountdown = BaiyeCatalogProbeInterval(); Probe(); }
            // 锁屏、长时间无人操作都不产生主动百业奖励。
            if (probeIdle)
            {
                FlushSegment(utc); manualSince = null; status = "无人操作 · 道韵结算暂停";
                return null;
            }
            if (probePathId == null)
            {
                FlushSegment(utc); manualSince = null;
                status = progress.Mode == 2 ? "未识别的前台应用 · 可在下方手动标记具体活动" : "未识别的前台应用";
                return null;
            }
            if (segmentPath != probePathId || segmentSource != probeSource) FlushSegment(utc);
            segmentPath = probePathId; segmentSource = probeSource;
            if (probeSource == "手动标记") { if (manualSince == null) manualSince = utc; } else manualSince = null;
            double credit = elapsed * BaiyeCatalog.DaoYunPerActiveSecond + carry;
            double whole = Math.Floor(credit);
            carry = credit - whole;
            string notice = null;
            if (whole > 0)
            {
                leveledUp = progress.AddYun(probePathId, whole, utc);
                segmentYun += whole;
                // 超长连续活动也定期落一条记录，保持获取记录可读。
                if (segmentYun >= 60) FlushSegment(utc);
                if (leveledUp)
                {
                    BaiyePathDef path = BaiyeCatalog.FindPath(probePathId);
                    notice = "道韵精进 · " + path.Name + "步入「" + BaiyeCatalog.LevelName(progress.LevelOf(probePathId)) + "」";
                }
            }
            status = BaiyeCatalog.FindPath(probePathId).Name + " · " + probeLabel + "（" + probeSource + "）";
            // 长期手动标记给出持续时长提示，避免误分类被遗忘。
            if (probeSource == "手动标记" && manualSince.HasValue)
            {
                double minutes = (utc - manualSince.Value).TotalMinutes;
                if (minutes >= 30) status += " · 手动标记已生效 " + ((int)minutes).ToString(CultureInfo.InvariantCulture) + " 分钟，不需要时可在道碑取消";
            }
            return notice;
        }
        private double BaiyeCatalogProbeInterval() { return ProbeIntervalSeconds > 0.05 ? ProbeIntervalSeconds : 2.0; }

        internal static string SelfTest()
        {
            int n = 0;
            Action<bool, string> check = delegate(bool okay, string label)
            {
                if (!okay) throw new InvalidOperationException("Baiye tracker: " + label);
                n++;
            };
            DateTime clock = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);
            Func<BaiyeTracker> make = delegate
            {
                BaiyeProgress p = new BaiyeProgress { Mode = 1, FirstRunExplained = true };
                BaiyeTracker t = new BaiyeTracker(p);
                t.ForegroundProcessName = delegate { return "devenv"; };
                t.IdleSeconds = delegate { return 0; };
                t.UtcNow = delegate { return clock; };
                return t;
            };
            bool leveled;
            // 关闭模式：完全不采样。
            BaiyeProgress offProgress = new BaiyeProgress { Mode = 0, FirstRunExplained = true };
            BaiyeTracker off = new BaiyeTracker(offProgress);
            off.ForegroundProcessName = delegate { return "devenv"; };
            off.IdleSeconds = delegate { return 0; };
            check(off.Tick(1, out leveled) == null && offProgress.Yun.Count == 0, "mode off never samples");
            // 底层隐私约束：未确认功能说明时，无论模式如何都绝不探测、不结算。
            BaiyeProgress unexplained = new BaiyeProgress { Mode = 2, ManualMarkId = "manual-zhen" };
            BaiyeTracker gated = new BaiyeTracker(unexplained);
            bool probed = false;
            gated.ForegroundProcessName = delegate { probed = true; return "devenv"; };
            gated.IdleSeconds = delegate { probed = true; return 0; };
            for (int i = 0; i < 30; i++) gated.Tick(1, out leveled);
            check(!probed && unexplained.Yun.Count == 0 && gated.StatusText.Contains("说明"), "no probing and no gains before the first-run acknowledgement");
            // 正常积累：660 秒编程（220 × 3 秒节拍）≈ 66 道韵，升入初窥门径。
            BaiyeTracker coding = make();
            string notice = null;
            for (int i = 0; i < 220; i++) { string step = coding.Tick(3, out leveled); if (step != null) notice = step; }
            BaiyeProgress codingProgress = ProgressOf(coding);
            check(codingProgress.YunOf("zhen") >= 64 && codingProgress.YunOf("zhen") <= 66.001, "660 active seconds credit about 66 dao-yun");
            check(codingProgress.LevelOf("zhen") == 1 && notice != null && notice.Contains("阵道"), "level-up notice names the dao");
            check(coding.StatusText.Contains("自动识别"), "status reports the recognized activity");
            // 无人操作：锁屏/空闲不产生奖励。
            BaiyeTracker idleTracker = make();
            idleTracker.IdleSeconds = delegate { return 600; };
            for (int i = 0; i < 300; i++) idleTracker.Tick(1, out leveled);
            check(ProgressOf(idleTracker).Yun.Count == 0 && idleTracker.StatusText.Contains("暂停"), "idle and lock screen never accrue");
            // 同一时间窗口只结算一种活动：编程与浏览器按节拍交替，总收益等于实际时长。
            BaiyeTracker mixed = make();
            bool dev = true;
            mixed.ForegroundProcessName = delegate { return dev ? "devenv" : "chrome"; };
            for (int i = 0; i < 220; i++) { dev = i % 2 == 0; mixed.Tick(3, out leveled); }
            BaiyeProgress mixedProgress = ProgressOf(mixed);
            check(mixedProgress.YunOf("zhen") + mixedProgress.YunOf("youli") >= 64.5 && mixedProgress.YunOf("zhen") + mixedProgress.YunOf("youli") <= 66.001,
                "alternating processes split one time window without double counting");
            check(mixedProgress.YunOf("zhen") >= 20 && mixedProgress.YunOf("youli") >= 20, "both alternating activities are credited");
            check(mixedProgress.Log.Count >= 2, "activity segments produce gain log entries");
            // 探测降频：660 个 1 秒节拍只探测约 330 次，结算总量不受影响。
            BaiyeTracker cadence = make();
            for (int i = 0; i < 660; i++) cadence.Tick(1, out leveled);
            check(cadence.ProbeCount >= 320 && cadence.ProbeCount <= 345, "foreground probing follows the sampling cadence, not the frame rate");
            check(ProgressOf(cadence).YunOf("zhen") >= 64.5 && ProgressOf(cadence).YunOf("zhen") <= 66.001, "cadence caching keeps activity time exact");
            // 手动标记：模式 1 忽略标记，模式 2 手动优先于自动识别。
            BaiyeTracker manualIgnored = make();
            ProgressOf(manualIgnored).ManualMarkId = "manual-zhan";
            manualIgnored.Tick(3, out leveled);
            check(ProgressOf(manualIgnored).YunOf("zhan") == 0, "category-only mode ignores manual marks");
            BaiyeProgress manualProgress = new BaiyeProgress { Mode = 2, FirstRunExplained = true, ManualMarkId = "manual-youli-learn" };
            BaiyeTracker manual = new BaiyeTracker(manualProgress);
            manual.ForegroundProcessName = delegate { return "devenv"; };
            manual.IdleSeconds = delegate { return 0; };
            manual.UtcNow = delegate { return clock; };
            for (int i = 0; i < 120; i++) manual.Tick(1, out leveled);
            check(manualProgress.YunOf("youli") >= 10.9 && manualProgress.YunOf("zhen") == 0, "manual mark overrides recognition in mark mode");
            check(manual.StatusText.Contains("手动标记"), "status reports the manual source");
            // 卡死节拍不能伪造大量道韵。
            BaiyeTracker frozen = make();
            frozen.Tick(3600, out leveled);
            check(ProgressOf(frozen).YunOf("zhen") <= BaiyeCatalog.MaxCreditSecondsPerTick * BaiyeCatalog.DaoYunPerActiveSecond + 0.001,
                "a stalled tick cannot fabricate hours of activity");
            // 未识别应用：无收益，提示手动兜底。
            BaiyeTracker unknown = make();
            unknown.ForegroundProcessName = delegate { return "mystery-app"; };
            unknown.Tick(1, out leveled);
            check(ProgressOf(unknown).Yun.Count == 0 && unknown.StatusText.Contains("未识别"), "unrecognized apps accrue nothing");
            // 片段封口：未结束的活动片段在 Seal 后落记录，重启后道韵与记录对应。
            BaiyeTracker sealing = make();
            for (int i = 0; i < 40; i++) sealing.Tick(1, out leveled);
            check(ProgressOf(sealing).Log.Count == 0, "an in-flight segment is not logged mid-way");
            sealing.Seal();
            check(ProgressOf(sealing).Log.Count == 1 && ProgressOf(sealing).Log[0].Amount >= 2.9, "Seal flushes the open segment into the gain log");
            // 崩溃补记：道韵多于记录合计时启动对账补一条历史记录。
            BaiyeProgress crashed = new BaiyeProgress();
            crashed.AddYun("wen", 25, clock);
            crashed.AddLog("wen", 6, "自动识别", clock);
            crashed.ReconcileLog(clock);
            check(crashed.Log.Count == 2 && crashed.Log[1].Source == "历史结算补记" && Math.Abs(crashed.Log[1].Amount - 19) < 0.2,
                "crash-lost segments get a catch-up record on startup");
            BaiyeProgress aligned = new BaiyeProgress();
            aligned.AddYun("wen", 6, clock); aligned.AddLog("wen", 6, "自动识别", clock);
            aligned.ReconcileLog(clock);
            check(aligned.Log.Count == 1, "consistent archives gain no spurious records");
            // 长期手动标记给出持续时长提示。
            BaiyeProgress markedProgress = new BaiyeProgress { Mode = 2, FirstRunExplained = true, ManualMarkId = "manual-zhan" };
            BaiyeTracker marked = new BaiyeTracker(markedProgress);
            marked.ForegroundProcessName = delegate { return "chrome"; };
            marked.IdleSeconds = delegate { return 0; };
            DateTime manualClock = clock;
            marked.UtcNow = delegate { return manualClock; };
            marked.Tick(1, out leveled);
            check(!marked.StatusText.Contains("取消"), "fresh manual marks show no nag");
            manualClock = manualClock.AddMinutes(45);
            marked.Tick(1, out leveled);
            check(marked.StatusText.Contains("45") && marked.StatusText.Contains("取消"), "long-lived manual marks surface their duration and a cancel hint");
            return "Baiye tracker: " + n + " checks passed (synthetic foreground/idle sources; no system state read).";
        }
        private static BaiyeProgress ProgressOf(BaiyeTracker tracker)
        {
            return tracker.progress;
        }
    }

    // 系统探针：唯一的系统接触面。只看前台窗口进程名与输入空闲时间；
    // 任何一步失败都返回「不可结算」，宁可少记也绝不窥探内容。
    internal static class BaiyeSystemProbe
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        internal static string GetForegroundProcessName()
        {
            try
            {
                IntPtr window = GetForegroundWindow();
                if (window == IntPtr.Zero) return null;
                uint pid;
                GetWindowThreadProcessId(window, out pid);
                if (pid == 0) return null;
                using (Process process = Process.GetProcessById((int)pid))
                    return process.ProcessName;
            }
            catch { return null; }
        }
        // 获取失败时按「无限空闲」处理（fail-closed：不确定就不结算）。
        internal static double GetIdleSeconds()
        {
            try
            {
                LASTINPUTINFO info = new LASTINPUTINFO();
                info.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(LASTINPUTINFO));
                if (!GetLastInputInfo(ref info)) return Double.MaxValue;
                uint elapsed = (uint)Environment.TickCount - info.dwTime; // 无符号回绕安全
                return elapsed / 1000.0;
            }
            catch { return Double.MaxValue; }
        }
    }
}
