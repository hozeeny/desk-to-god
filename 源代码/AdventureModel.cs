using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace NetBunny
{
    internal sealed class AdventureWeapon
    {
        internal string Id, Name, Description;
        internal double Range, Cooldown;
        internal int Damage;
        internal bool Area;
        internal AdventureWeapon(string id, string name, string description, double range, int damage, double cooldown, bool area)
        { Id = id; Name = name; Description = description; Range = range; Damage = damage; Cooldown = cooldown; Area = area; }
        internal static readonly AdventureWeapon[] All = {
            new AdventureWeapon("spear", "星辉长枪", "近身突刺，稳健的初始武器", 110, 24, .58, false),
            new AdventureWeapon("greatsword", "虹光巨剑", "范围斩击，仅伤害已授权的排队怪物", 155, 36, .9, true),
            new AdventureWeapon("wave", "破空冲击波", "远距离能量冲击，贯穿已排队的目标", 380, 27, .75, true),
            new AdventureWeapon("laser", "极光镭射枪", "远程快速攻击已排队的目标", 650, 19, .32, false),
            new AdventureWeapon("boomerang", "月轮回旋镖", "回旋斩击附近已排队的目标", 300, 32, .8, true),
            new AdventureWeapon("blackhole", "星渊黑洞", "范围吸收；所有武器都可使用独立的一键清场", 900, 65, 1.3, true),
            new AdventureWeapon("automaton", "自动攻击装置", "连续能量弹；自动选怪需在装备栏明确开启", 480, 20, .4, false)
        };
        internal static AdventureWeapon Find(string id) { return All.FirstOrDefault(w => w.Id == id) ?? All[0]; }
    }
    internal enum AdventureAuthorization { None, Manual, Automatic, ClearField }
    internal sealed class AdventureMonster
    {
        internal string Id, Name;
        internal double HealthFactor, WalkSpeed, AttackCooldown;
        internal int Damage;
        private AdventureMonster(string id, string name, double health, double speed, int damage, double cooldown)
        { Id = id; Name = name; HealthFactor = health; WalkSpeed = speed; Damage = damage; AttackCooldown = cooldown; }
        internal static readonly AdventureMonster[] All = {
            new AdventureMonster("skeleton", "骷髅卫兵", 1, 68, 4, 2.4),
            new AdventureMonster("slime", "翡翠史莱姆", 1.18, 52, 3, 2.8),
            new AdventureMonster("imp", "独眼小恶魔", .96, 80, 6, 2.6),
            new AdventureMonster("bat", "暮光蝙蝠", .78, 102, 3, 1.9),
            new AdventureMonster("mushroom", "魔法蘑菇怪", 1.08, 58, 5, 2.7)
        };
        internal static AdventureMonster Find(string id) { return All.FirstOrDefault(m => m.Id == id) ?? All[0]; }
        internal static AdventureEnemy Create(CleanupBatch batch, Random random)
        {
            if (batch == null) throw new ArgumentNullException("batch");
            AdventureMonster type = All[random.Next(All.Length)];
            AdventureEnemy enemy = new AdventureEnemy { Id = batch.Id, Batch = batch, MonsterKind = type.Id };
            enemy.MaxHealth = Math.Max(20, (int)((45 + Math.Min(140, batch.Bytes / (12 * 1024 * 1024))) * type.HealthFactor));
            enemy.Health = enemy.MaxHealth; return enemy;
        }
    }
    internal sealed class AdventureEnemy
    {
        internal string Id;
        internal CleanupBatch Batch;
        internal bool IsBoss, IsDiskBoss, Authorized, Cleaning, CleanupStarted, Failed, Defeated;
        internal bool VisualDefeated, VisualDirty = true, FacingRight = true, Dragging;
        internal bool KillRewarded;
        internal int KillExperience;
        internal const double ShatterSeconds = .30;
        internal double DeathElapsed, MoveSpeed;
        internal int PatrolSector = -1;
        internal string MonsterKind = "skeleton";
        internal AdventureAuthorization Authorization;
        internal int MaxHealth, Health, QueueNumber;
        internal double X, Y, TargetX, TargetY, AnimationTime, AttackTime, VanishTime, HitFlash, WaitingSeconds;
        internal string Message;
        internal long Bytes { get { return Batch == null ? 0 : Batch.Bytes; } }
        internal bool IsDiskWarning { get { return IsDiskBoss && (Batch == null || Batch.FilesCount == 0); } }
        internal bool IsVisuallyHidden { get { return VisualDefeated && DeathElapsed >= ShatterSeconds; } }
        internal void StartShatter()
        {
            if (!VisualDefeated) { VisualDefeated = true; DeathElapsed = 0; }
            VisualDirty = true; VanishTime = Math.Max(0, ShatterSeconds - DeathElapsed);
        }
        internal void RestoreVisual(string message)
        {
            // A cleanup callback or mode switch can update status, never revive a
            // monster whose combat death has already begun.
            if (VisualDefeated || Defeated || Health <= 0)
            { Health = 0; Message = message; StartShatter(); return; }
            VisualDefeated = false; DeathElapsed = 0; VanishTime = 0; VisualDirty = true; HitFlash = 0; MoveSpeed = 0;
            Health = Math.Max(1, Health); Message = message;
        }
        internal bool CancelBeforeCleanup(string message)
        {
            // Started work keeps its authority until the real result is accounted.
            // The return value identifies a newly retired, already defeated target.
            if (CleanupStarted || Defeated) return false;
            Authorized = false; Authorization = AdventureAuthorization.None; Cleaning = false; QueueNumber = 0;
            if (VisualDefeated || Health <= 0)
            {
                Defeated = Failed = true; Health = 0;
                Message = message + "；怪物已击败，未执行文件清理，剩余文件待重新扫描";
                StartShatter(); return true;
            }
            RestoreVisual(message + " · 可重新点选"); return false;
        }
        internal static void ChoosePatrolTarget(AdventureEnemy enemy, Random random, double left, double top, double width, double height)
        {
            // Stepping by four visits every tile of this 3 x 3 grid before repeating.
            // Each enemy starts in a different random tile and has its own jitter.
            enemy.PatrolSector = enemy.PatrolSector < 0 ? random.Next(9) : (enemy.PatrolSector + 4) % 9;
            double u = (enemy.PatrolSector % 3 + .12 + random.NextDouble() * .76) / 3;
            double v = (enemy.PatrolSector / 3 + .12 + random.NextDouble() * .76) / 3;
            enemy.TargetX = left + Math.Max(0, width) * u; enemy.TargetY = top + Math.Max(0, height) * v;
        }
    }
    internal sealed class AdventureProgress
    {
        internal int Level = 1, Experience, Score;
        internal int Health = 100;
        internal long FreedBytes;
        internal bool Dead;
        internal string WeaponId = "spear", ThemeMode = "AUTO", LastThemeCode = "OTHER";
        internal List<string> Inventory = new List<string> { "spear" };
        internal int MaxHealth { get { return 100 + (Level - 1) * 12; } }
        internal int NextLevelExperience { get { return 60 + (Level - 1) * 35; } }
        internal static AdventureProgress Load(string path)
        {
            AdventureProgress p = new AdventureProgress();
            try
            {
                if (!File.Exists(path)) return p;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int equals = line.IndexOf('='); if (equals < 1) continue;
                    string k = line.Substring(0, equals), v = line.Substring(equals + 1); int number; long large;
                    if (k == "level" && int.TryParse(v, out number)) p.Level = Math.Max(1, Math.Min(100, number));
                    else if (k == "experience" && int.TryParse(v, out number)) p.Experience = Math.Max(0, number);
                    else if (k == "score" && int.TryParse(v, out number)) p.Score = Math.Max(0, number);
                    else if (k == "health" && int.TryParse(v, out number)) p.Health = Math.Max(0, number);
                    else if (k == "freed" && long.TryParse(v, out large)) p.FreedBytes = Math.Max(0, large);
                    else if (k == "dead") p.Dead = v == "1";
                    else if (k == "weapon") p.WeaponId = AdventureWeapon.Find(v).Id;
                    else if (k == "inventory") p.Inventory = v.Split(',').Where(id => AdventureWeapon.All.Any(w => w.Id == id)).Distinct().ToList();
                    else if (k == "lasttheme" && new[] { "CN", "JP", "US", "TW", "HK", "OTHER" }.Contains(v)) p.LastThemeCode = v;
                    else if (k == "theme" && new[] { "AUTO", "OFF", "CN", "JP", "US", "TW", "HK", "OTHER" }.Contains(v)) p.ThemeMode = v;
                }
                if (!p.Inventory.Contains("spear")) p.Inventory.Insert(0, "spear");
                if (!p.Inventory.Contains(p.WeaponId)) p.WeaponId = "spear";
                p.Experience = Math.Min(p.Experience, p.NextLevelExperience - 1);
                p.Health = p.Dead ? 0 : Math.Max(1, Math.Min(p.MaxHealth, p.Health));
            }
            catch { return new AdventureProgress(); }
            return p;
        }
        internal void Save(string path)
        {
            string folder = Path.GetDirectoryName(path); if (!String.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            string temporary = path + ".new";
            string data = "version=1\nlevel=" + Level + "\nexperience=" + Experience + "\nscore=" + Score + "\nhealth=" + Health + "\nfreed=" + FreedBytes + "\ndead=" + (Dead ? "1" : "0") + "\nweapon=" + WeaponId + "\ninventory=" + String.Join(",", Inventory.ToArray()) + "\ntheme=" + ThemeMode + "\nlasttheme=" + LastThemeCode + "\n";
            File.WriteAllText(temporary, data, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
    }
    internal sealed class AdventureBattle
    {
        internal readonly List<AdventureEnemy> Enemies = new List<AdventureEnemy>();
        private readonly List<AdventureEnemy> queue = new List<AdventureEnemy>();
        internal readonly AdventureProgress Progress;
        internal const string DiskWarningMessage = "磁盘空间不足，但当前没有可安全清理的临时文件；此 Boss 仅作警示，不会凭空释放空间";
        internal double LevelGlow, AttackEffect, DeathTime, Cooldown;
        internal string LastReward = "点击怪物依次出击，或在装备栏选择自动规划 / 一键清场";
        internal bool Automatic;
        private readonly Random killRandom;
        private bool pressureArmed = true;
        private double pressureSeconds, lastBossAt = -1000;
        internal AdventureBattle(AdventureProgress progress, Random killRandom = null) { Progress = progress; this.killRandom = killRandom ?? new Random(); }
        internal AdventureWeapon Weapon { get { return AdventureWeapon.Find(Progress.WeaponId); } }
        internal AdventureEnemy Target { get { return queue.FirstOrDefault(e => !e.Defeated && !e.Cleaning && !e.Failed && !e.IsDiskWarning); } }
        internal int QueueCount { get { return queue.Count(e => !e.Defeated && !e.Failed); } }
        internal bool Authorize(AdventureEnemy enemy)
        {
            return Authorize(enemy, AdventureAuthorization.Manual);
        }
        private bool Authorize(AdventureEnemy enemy, AdventureAuthorization source)
        {
            if (Progress.Dead || enemy == null || enemy.Defeated || enemy.CleanupStarted || enemy.Failed) return false;
            if (enemy.IsDiskWarning) { LastReward = DiskWarningMessage; return false; }
            if (queue.Contains(enemy))
            {
                if (source != AdventureAuthorization.Manual || enemy.Authorization != AdventureAuthorization.Automatic) return false;
                queue.Remove(enemy);
            }
            else if (enemy.Cleaning) return false;
            enemy.Authorized = true; enemy.Authorization = source;
            if (source == AdventureAuthorization.Manual)
            {
                int automaticAt = queue.FindIndex(e => e.Authorization == AdventureAuthorization.Automatic && !e.CleanupStarted);
                if (automaticAt < 0) queue.Add(enemy); else queue.Insert(automaticAt, enemy);
            }
            else queue.Add(enemy);
            Renumber(); return true;
        }
        internal void SetAutomatic(bool enabled)
        {
            Automatic = enabled && !Progress.Dead;
            if (Automatic) return;
            bool retired = false;
            foreach (AdventureEnemy enemy in queue.ToArray())
                if (enemy.Authorization == AdventureAuthorization.Automatic && !enemy.CleanupStarted)
                { retired |= enemy.CancelBeforeCleanup("自动清理已取消"); queue.Remove(enemy); }
            if (retired) LastReward = "自动清理已取消；已击败的怪物继续消散，未执行文件清理，剩余文件待重新扫描";
            Renumber();
        }
        internal AdventureEnemy PlanNext(double heroX, double heroY)
        {
            // A live target stays selected until it finishes or the user gives a manual order.
            if (!Automatic || Progress.Dead || Target != null || queue.Any(e => e.Cleaning && !e.Defeated && !e.Failed)) return Target;
            AdventureEnemy next = Enemies.Where(e => !e.Authorized && !e.Failed && !e.Defeated && !e.Cleaning && !e.IsDiskWarning)
                .OrderBy(e => PlanScore(e, heroX, heroY)).ThenBy(e => e.Id, StringComparer.Ordinal).FirstOrDefault();
            if (next != null) Authorize(next, AdventureAuthorization.Automatic);
            else if (Enemies.Any(e => e.IsDiskWarning && !e.Defeated)) LastReward = DiskWarningMessage;
            return Target;
        }
        private static double PlanScore(AdventureEnemy enemy, double heroX, double heroY)
        {
            double distance = Distance(heroX, heroY, enemy.X, enemy.Y);
            double health = Math.Max(0, enemy.Health) / (double)Math.Max(1, enemy.MaxHealth);
            double threat = enemy.IsBoss && distance < 280 ? 260 : AdventureMonster.Find(enemy.MonsterKind).Damage * (distance < 180 ? 12 : 0);
            // Waiting age grows without a hard cap so a distant target cannot starve forever.
            return distance + health * 170 + (enemy.IsBoss ? 160 : 0) - threat - enemy.WaitingSeconds * 14;
        }
        internal List<AdventureEnemy> ClearFieldSnapshot()
        {
            List<AdventureEnemy> selected = new List<AdventureEnemy>();
            if (Progress.Dead) return selected;
            Automatic = false; // This action authorizes one fixed snapshot, never future spawns.
            foreach (AdventureEnemy enemy in Enemies.ToArray())
            {
                if (enemy.Defeated || enemy.Failed) continue;
                if (enemy.IsDiskWarning) { LastReward = DiskWarningMessage; continue; }
                if (enemy.Cleaning) { if (enemy.Authorized) enemy.Authorization = AdventureAuthorization.ClearField; continue; }
                if (!enemy.Authorized) Authorize(enemy, AdventureAuthorization.ClearField);
                if (!enemy.Authorized) continue;
                enemy.Authorization = AdventureAuthorization.ClearField; enemy.Health = 0; enemy.HitFlash = .25; enemy.Cleaning = true; enemy.StartShatter(); GrantKillExperience(enemy); selected.Add(enemy);
            }
            AttackEffect = 1; Renumber(); selected.Sort(delegate(AdventureEnemy a, AdventureEnemy b) { return a.QueueNumber.CompareTo(b.QueueNumber); }); return selected;
        }
        internal void CancelQueue()
        {
            bool retired = false;
            foreach (AdventureEnemy e in queue) { if (!e.CleanupStarted) retired |= e.CancelBeforeCleanup("待办清理已取消"); e.QueueNumber = 0; }
            queue.Clear(); Automatic = false;
            if (retired) LastReward = "待办清理已取消；已击败的怪物继续消散，未执行文件清理，剩余文件待重新扫描";
        }
        internal void StopFightingAfterDeath()
        {
            Automatic = false;
            foreach (AdventureEnemy enemy in queue.ToArray())
            {
                if (enemy.CleanupStarted || (enemy.VisualDefeated && enemy.Cleaning)) continue;
                enemy.CancelBeforeCleanup("骑士战死，攻击已停止"); queue.Remove(enemy);
            }
            Renumber();
        }
        private void Renumber() { int index = 0; foreach (AdventureEnemy e in queue) e.QueueNumber = e.Defeated || e.Failed ? 0 : ++index; }
        internal List<AdventureEnemy> Strike(AdventureEnemy primary)
        {
            List<AdventureEnemy> defeated = new List<AdventureEnemy>();
            if (Progress.Dead || primary == null || !primary.Authorized || primary.Cleaning || primary.Failed || primary.Defeated || primary.IsDiskWarning || Cooldown > 0) return defeated;
            AdventureWeapon weapon = Weapon; Cooldown = weapon.Cooldown; AttackEffect = 1;
            List<AdventureEnemy> targets = new List<AdventureEnemy> { primary };
            if (weapon.Area)
                targets.AddRange(queue.Where(e => e != primary && !e.Cleaning && !e.Failed && !e.Defeated && !e.IsDiskWarning && e.Authorized && Distance(e.X, e.Y, primary.X, primary.Y) <= (weapon.Id == "blackhole" ? 3000 : weapon.Range * .8)));
            foreach (AdventureEnemy enemy in targets)
            {
                enemy.Health = Math.Max(0, enemy.Health - weapon.Damage - (Progress.Level - 1) * 3); enemy.HitFlash = .25; enemy.VisualDirty = true;
                if (enemy.Health == 0) { enemy.Cleaning = true; enemy.StartShatter(); GrantKillExperience(enemy); defeated.Add(enemy); }
            }
            return defeated;
        }
        internal bool GrantKillExperience(AdventureEnemy enemy)
        {
            if (enemy == null || !enemy.Authorized || enemy.Health > 0 || !enemy.VisualDefeated || enemy.Defeated || enemy.IsDiskWarning || enemy.KillRewarded) return false;
            enemy.KillRewarded = true; enemy.KillExperience = killRandom.Next(2, 4);
            Progress.Experience += enemy.KillExperience; Progress.Score += enemy.KillExperience * 5;
            LastReward = "击败怪物 · 经验 +" + enemy.KillExperience + "；文件清理另行结算";
            int priorLevel = Progress.Level;
            while (Progress.Level < 100 && Progress.Experience >= Progress.NextLevelExperience)
            { Progress.Experience -= Progress.NextLevelExperience; Progress.Level++; if (!Progress.Dead) Progress.Health = Progress.MaxHealth; LevelGlow = 4.5; }
            if (Progress.Level == 100) Progress.Experience = Math.Min(Progress.Experience, Progress.NextLevelExperience - 1);
            if (Progress.Level > priorLevel)
            {
                AdventureWeapon reward = AdventureWeapon.All.FirstOrDefault(w => !Progress.Inventory.Contains(w.Id));
                if (reward != null) { Progress.Inventory.Add(reward.Id); LastReward += " · 获得「" + reward.Name + "」"; }
                LastReward += " · 升至 Lv." + Progress.Level;
            }
            return true;
        }
        internal void Complete(AdventureEnemy enemy, long freedBytes, int deletedFiles, string message, double randomDrop, bool cancelled = false)
        {
            if (enemy == null || !enemy.Cleaning || !enemy.Authorized || enemy.Defeated) return;
            enemy.Cleaning = false; enemy.CleanupStarted = false; queue.Remove(enemy); enemy.QueueNumber = 0;
            if (enemy.IsDiskWarning)
            {
                enemy.Authorized = false; enemy.Authorization = AdventureAuthorization.None; enemy.RestoreVisual(DiskWarningMessage);
                LastReward = DiskWarningMessage; Renumber(); return;
            }
            if (enemy.IsBoss && !enemy.IsDiskBoss)
            {
                enemy.Defeated = true; enemy.Health = 0; enemy.StartShatter(); Progress.Score += 100;
                LastReward = "击败内存压力 Boss +100 战斗分；系统内存不会因此被清空"; Renumber(); return;
            }
            if (enemy.IsDiskBoss && deletedFiles <= 0) freedBytes = 0;
            if (freedBytes == 0 && deletedFiles > 0)
            {
                // Empty files are valid cleanup successes, but have no released bytes
                // with which to award any additional cleanup bonus or disk statistics.
                enemy.Defeated = true; enemy.Failed = false; enemy.Health = 0; enemy.StartShatter();
                enemy.Message = "已删除 " + deletedFiles + " 个空文件 · 0 B；击杀经验已单独结算";
                if (cancelled) enemy.Message += "；剩余文件已取消，可重新扫描";
                LastReward = enemy.Message; Renumber(); return;
            }
            if (freedBytes <= 0)
            {
                enemy.Defeated = enemy.Failed = true; enemy.Health = 0; enemy.Authorized = false; enemy.Authorization = AdventureAuthorization.None;
                enemy.StartShatter();
                LastReward = cancelled ? "怪物已击败，文件清理已取消；未释放空间，击杀经验保留，剩余文件待重新扫描" : "怪物已击败，文件暂不能清理；未释放空间，击杀经验保留，剩余文件待重新扫描";
                enemy.Message = String.IsNullOrEmpty(message) ? LastReward : LastReward + "（" + message + "）";
                Renumber(); return;
            }
            enemy.Defeated = true; enemy.Health = 0; enemy.StartShatter();
            Progress.FreedBytes = checked(Progress.FreedBytes + Math.Max(0, freedBytes));
            LastReward = "实际清理 " + FormatBytes(freedBytes) + "；击杀经验已单独结算";
            if (enemy.IsDiskBoss) { Progress.Score += 100; LastReward = "击败磁盘危机 Boss · " + LastReward + " · 战斗分 +100"; }
            if (cancelled) LastReward += " · 剩余文件已取消，可重新扫描";
            AdventureWeapon reward = null;
            if (reward == null && randomDrop < .28)
                reward = AdventureWeapon.All.FirstOrDefault(w => !Progress.Inventory.Contains(w.Id));
            if (reward != null) { Progress.Inventory.Add(reward.Id); LastReward += " · 获得「" + reward.Name + "」"; }
            Renumber();
        }
        internal void Damage(int amount)
        {
            if (Progress.Dead) return;
            Progress.Health = Math.Max(0, Progress.Health - Math.Max(0, amount));
            if (Progress.Health == 0) { Progress.Dead = true; DeathTime = 0; Automatic = false; LastReward = "骑士战死。右键墓碑打开装备栏，选择复活"; }
        }
        internal void Revive() { Progress.Dead = false; Progress.Health = Progress.MaxHealth; DeathTime = 0; LastReward = "骑士已复活，继续冒险"; }
        internal void Tick(double dt)
        {
            Cooldown = Math.Max(0, Cooldown - dt); AttackEffect = Math.Max(0, AttackEffect - dt * 3); LevelGlow = Math.Max(0, LevelGlow - dt);
            if (Progress.Dead) DeathTime += dt;
            foreach (AdventureEnemy e in Enemies)
            {
                e.AnimationTime += dt; if (!e.Authorized && !e.Defeated && !e.Failed) e.WaitingSeconds += Math.Max(0, dt);
                e.HitFlash = Math.Max(0, e.HitFlash - dt);
                if (e.VisualDefeated) { e.DeathElapsed += Math.Max(0, dt); e.VanishTime = Math.Max(0, AdventureEnemy.ShatterSeconds - e.DeathElapsed); }
            }
        }
        internal bool ObserveMemory(double percent, double dt, double now)
        {
            if (percent < 78) { pressureArmed = true; pressureSeconds = 0; }
            else if (percent >= 85) pressureSeconds += Math.Max(0, Math.Min(10, dt)); else pressureSeconds = 0;
            if (pressureArmed && pressureSeconds >= 12 && now - lastBossAt >= 180 && !Enemies.Any(e => e.IsBoss && !e.IsDiskBoss && !e.Defeated))
            { pressureArmed = false; lastBossAt = now; pressureSeconds = 0; return true; }
            return false;
        }
        internal static double AdvanceCycle(double phase, double dt, double speed) { return speed <= 0 ? phase : (phase + Math.Max(0, Math.Min(.1, dt)) * (.65 + Math.Min(2.6, speed / 90))) % 1; }
        internal static double Distance(double x, double y, double xx, double yy) { double dx = x - xx, dy = y - yy; return Math.Sqrt(dx * dx + dy * dy); }
        internal static string FormatBytes(long bytes)
        {
            if (bytes >= 1073741824) return (bytes / 1073741824.0).ToString("0.00", CultureInfo.InvariantCulture) + " GiB";
            if (bytes >= 1048576) return (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MiB";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KiB";
            return Math.Max(0, bytes).ToString(CultureInfo.InvariantCulture) + " B";
        }
        internal static string SelfTest()
        {
            int checks = 0; Action<bool, string> check = delegate(bool ok, string text) { if (!ok) throw new InvalidOperationException("Adventure: " + text); checks++; };
            double slowPhase = AdvanceCycle(.2, .016, 20), fastPhase = AdvanceCycle(slowPhase, .016, 500);
            check(Math.Abs(fastPhase - slowPhase - .016 * 3.25) < .000001, "speed changes advance phase continuously, never multiply total elapsed time");
            check(AdvanceCycle(fastPhase, .1, 0) == fastPhase, "resting preserves the last running phase");
            AdventureProgress p = new AdventureProgress(); AdventureBattle m = new AdventureBattle(p);
            AdventureEnemy a = new AdventureEnemy { Id = "a", Health = 20, MaxHealth = 20 }, b = new AdventureEnemy { Id = "b", Health = 20, MaxHealth = 20 }, c = new AdventureEnemy { Id = "c", Health = 20, MaxHealth = 20 };
            m.Enemies.AddRange(new[] { a, b, c }); m.Authorize(b); m.Authorize(a); check(m.Target == b && b.QueueNumber == 1 && a.QueueNumber == 2, "click order FIFO");
            check(!m.Authorize(b) && m.QueueCount == 2, "duplicate selection ignored"); p.WeaponId = "greatsword";
            List<AdventureEnemy> hit = m.Strike(b); check(hit.Count == 2 && c.Health == 20 && !c.Authorized, "area weapon never touches unapproved targets");
            int areaKillExperience = a.KillExperience + b.KillExperience;
            m.Complete(b, 0, 0, "locked", 0); check(p.Experience == areaKillExperience && p.FreedBytes == 0 && b.Failed && b.Defeated, "failed deletion preserves combat XP without faking released space or resurrecting the target");
            m.Complete(a, 1048576, 1, "ok", 1); check(p.FreedBytes == 1048576 && p.Experience == areaKillExperience, "successful cleanup records real bytes without awarding XP a second time");
            int oldScore = p.Score; m.Complete(a, 1048576, 1, "duplicate", 0); check(p.Score == oldScore, "duplicate completion ignored");
            AdventureProgress emptyProgress = new AdventureProgress { Experience = 59, Score = 321, FreedBytes = 1234 };
            AdventureBattle emptyBattle = new AdventureBattle(emptyProgress);
            AdventureEnemy emptyFiles = new AdventureEnemy { Id = "empty-files", Health = 1, MaxHealth = 1 }, afterEmpty = new AdventureEnemy { Id = "after-empty", Health = 20, MaxHealth = 20 };
            emptyBattle.Enemies.AddRange(new[] { emptyFiles, afterEmpty }); emptyBattle.Authorize(emptyFiles); emptyBattle.Authorize(afterEmpty); emptyBattle.Strike(emptyFiles);
            int emptyKillLevel = emptyProgress.Level, emptyKillXP = emptyProgress.Experience, emptyKillScore = emptyProgress.Score, emptyKillInventory = emptyProgress.Inventory.Count;
            emptyBattle.Complete(emptyFiles, 0, 3, "deleted empty files", 0);
            check(emptyFiles.Defeated && !emptyFiles.Failed && !emptyFiles.Cleaning && emptyFiles.VanishTime > 0 && emptyBattle.Target == afterEmpty && emptyBattle.QueueCount == 1, "deleted empty files finish successfully and unblock the next target");
            check(emptyProgress.Level == emptyKillLevel && emptyProgress.Experience == emptyKillXP && emptyProgress.Score == emptyKillScore && emptyProgress.FreedBytes == 1234 && emptyProgress.Inventory.Count == emptyKillInventory && emptyBattle.LastReward.Contains("3 个空文件") && emptyBattle.LastReward.Contains("0 B"), "zero-byte success reports file count without extra XP, score, drops, or fabricated disk bytes");
            string emptyNotice = emptyBattle.LastReward; emptyBattle.Complete(emptyFiles, 1048576, 1, "duplicate result", 0);
            check(emptyProgress.Experience == emptyKillXP && emptyProgress.Score == emptyKillScore && emptyProgress.FreedBytes == 1234 && emptyProgress.Inventory.Count == emptyKillInventory && emptyBattle.LastReward == emptyNotice, "empty-file completion is idempotent even if a later callback claims nonzero bytes");
            AdventureEnemy noDeletion = new AdventureEnemy { Cleaning = true, Authorized = true };
            emptyBattle.Complete(noDeletion, 0, 0, "nothing deleted", 0);
            check(noDeletion.Failed && noDeletion.Defeated && noDeletion.Health == 0 && emptyProgress.Experience == emptyKillXP, "zero bytes with zero deleted files retires the defeated monster as an unrewarded cleanup failure");
            m.Damage(10000); check(p.Dead && p.Health == 0 && !m.Automatic, "combat death stops automation"); m.Tick(10); check(p.Dead, "time and connection cannot revive combat death"); m.Revive(); check(!p.Dead && p.Health == p.MaxHealth, "explicit revival");
            bool boss = false; for (int i = 0; i < 3; i++) boss |= m.ObserveMemory(90, 5, 200 + i * 5); check(boss, "sustained RAM pressure boss");
            check(!m.ObserveMemory(95, 10, 500), "continued pressure does not repeatedly spawn"); m.ObserveMemory(60, 5, 501);
            check(!m.ObserveMemory(90, 5, 502), "recovery hysteresis resets accumulation"); m.ObserveMemory(90, 5, 507); check(m.ObserveMemory(90, 5, 512), "new sustained pressure permits later boss");
            m.Authorize(c); m.CancelQueue(); check(m.QueueCount == 0 && !c.Authorized, "cancel clears pending authorization");
            AdventureProgress fallen = new AdventureProgress { Dead = true, Health = 0, Experience = 58 }; AdventureBattle postDeath = new AdventureBattle(fallen);
            AdventureEnemy pending = new AdventureEnemy { Cleaning = true, Authorized = true };
            postDeath.Complete(pending, 1048576, 1, "async completion", 1);
            check(fallen.Level == 1 && fallen.Experience == 58 && fallen.FreedBytes == 1048576 && fallen.Dead && fallen.Health == 0, "cleanup finishing after hero death records bytes without inventing kill XP or resurrecting the hero");
            check(fallen.Inventory.Count == 1, "cleanup callback alone cannot fabricate a kill level or guaranteed weapon unlock");
            AdventureBattle planner = new AdventureBattle(new AdventureProgress());
            AdventureEnemy near = new AdventureEnemy { Id = "near", Health = 90, MaxHealth = 100, X = 50 }, low = new AdventureEnemy { Id = "low", Health = 5, MaxHealth = 100, X = 75 }, far = new AdventureEnemy { Id = "far", Health = 100, MaxHealth = 100, X = 1200 };
            planner.Enemies.AddRange(new[] { near, low, far });
            check(!planner.Automatic && planner.PlanNext(0, 0) == null && !near.Authorized, "automatic planning is opt-in");
            planner.SetAutomatic(true); check(planner.PlanNext(0, 0) == low && low.Authorization == AdventureAuthorization.Automatic, "planner weighs nearby low-health targets");
            near.X = 1; near.Health = 1; check(planner.PlanNext(0, 0) == low && planner.QueueCount == 1, "automatic target remains stable while moving");
            planner.Authorize(far); planner.Authorize(near); check(planner.Target == far && far.QueueNumber == 1 && near.QueueNumber == 2 && low.QueueNumber == 3, "manual FIFO orders preempt automatic planning");
            planner.SetAutomatic(false); check(planner.Target == far && planner.QueueCount == 2 && !low.Authorized, "disabling automation preserves manual targets only");
            planner.CancelQueue(); planner.SetAutomatic(true); planner.PlanNext(0, 0); AdventureEnemy selectedAuto = planner.Target;
            check(planner.Authorize(selectedAuto) && selectedAuto.Authorization == AdventureAuthorization.Manual, "manual click upgrades an automatic target authorization");
            planner.SetAutomatic(false); check(planner.Target == selectedAuto && selectedAuto.Authorized, "converted manual target survives disabling automatic mode");
            planner.CancelQueue(); far.WaitingSeconds = 200; planner.SetAutomatic(true); check(planner.PlanNext(0, 0) == far, "waiting age eventually prevents starvation");
            far.Cleaning = true; far.Health = 0; planner.SetAutomatic(false); check(!far.Cleaning && !far.Authorized && far.Health == 0 && far.Defeated && far.VisualDefeated, "mode switch retires lethal automatic jobs before their worker starts without resurrection");
            planner.SetAutomatic(true); planner.PlanNext(0, 0); AdventureEnemy executing = planner.Target; executing.Cleaning = executing.CleanupStarted = true;
            planner.SetAutomatic(false); check(executing.Authorized && executing.Cleaning, "mode switch preserves in-flight cleanup accounting");
            planner.Complete(executing, 4096, 1, "synthetic", 1); check(planner.Progress.FreedBytes == 4096, "in-flight real success is accounted after automatic mode stops");
            AdventureBattle ties = new AdventureBattle(new AdventureProgress());
            AdventureEnemy tieZ = new AdventureEnemy { Id = "z", Health = 20, MaxHealth = 20, X = 60 }, tieA = new AdventureEnemy { Id = "a", Health = 20, MaxHealth = 20, X = 60 };
            ties.Enemies.AddRange(new[] { tieZ, tieA }); ties.SetAutomatic(true); check(ties.PlanNext(0, 0) == tieA, "planning tie-break is deterministic");
            AdventureBattle sweep = new AdventureBattle(new AdventureProgress());
            AdventureEnemy sweepA = new AdventureEnemy { Id = "sweep-a", Health = 80, MaxHealth = 80 }, sweepB = new AdventureEnemy { Id = "sweep-b", Health = 80, MaxHealth = 80 }, sweepBoss = new AdventureEnemy { Id = "sweep-boss", IsBoss = true, MonsterKind = "boss", Health = 900, MaxHealth = 900 };
            sweep.Enemies.AddRange(new[] { sweepA, sweepB, sweepBoss }); sweep.SetAutomatic(true);
            List<AdventureEnemy> clear = sweep.ClearFieldSnapshot();
            check(clear.Count == 3 && clear.All(e => e.Authorized && e.Cleaning && e.Health == 0) && !sweep.Automatic, "clear field needs no unlocked weapon and stops automatic expansion");
            AdventureEnemy future = new AdventureEnemy { Id = "future", Health = 20, MaxHealth = 20 }; sweep.Enemies.Add(future); sweep.Tick(30); sweep.PlanNext(0, 0);
            check(!future.Authorized && future.Health == 20, "one-click clear does not authorize later spawns");
            int clearXP = sweepA.KillExperience + sweepB.KillExperience + sweepBoss.KillExperience;
            check(sweep.Progress.FreedBytes == 0 && sweep.Progress.Experience == clearXP && clearXP >= 6 && clearXP <= 9, "clear awards one 2-3 XP kill per target but never claims cleanup bytes");
            sweep.Complete(sweepBoss, 0, 0, null, 1); check(sweep.Progress.Score == 100 + clearXP * 5 && sweep.Progress.FreedBytes == 0 && sweep.Progress.Experience == clearXP, "memory boss adds combat score without faking RAM or duplicating kill XP");
            sweep.Complete(sweepA, 0, 0, "file busy", 0); check(sweepA.Failed && sweepA.Defeated && sweep.Progress.Experience == clearXP, "one-click cleanup failure preserves its already earned kill XP");
            sweep.CancelQueue(); check(!sweepB.Authorized && !sweepB.Cleaning && sweepB.Health == 0 && sweepB.Defeated && sweepB.IsVisuallyHidden, "cancel stops unstarted one-click batches without replaying or undoing their completed death");
            sweep.Damage(99999); check(sweep.ClearFieldSnapshot().Count == 0 && !sweep.Authorize(future), "dead hero cannot authorize a new clear or manual target");
            sweep.SetAutomatic(true); check(!sweep.Automatic && sweep.PlanNext(0, 0) == null, "death prevents automatic restart");
            sweep.Revive(); check(!sweep.Automatic, "manual revival leaves automatic mode off");
            Random seeded = new Random(527); HashSet<string> kinds = new HashSet<string>(); bool stableKinds = true;
            CleanupBatch emptyBatch = new CleanupBatch(new List<CleanupCandidate>(), 1);
            for (int i = 0; i < 80; i++)
            {
                AdventureEnemy monster = AdventureMonster.Create(emptyBatch, seeded); kinds.Add(monster.MonsterKind);
                string original = monster.MonsterKind; AdventureBattle visual = new AdventureBattle(new AdventureProgress()); visual.Enemies.Add(monster); visual.Tick(1);
                stableKinds &= monster.MonsterKind == original && monster.Health == monster.MaxHealth && monster.MaxHealth >= 20;
            }
            check(stableKinds, "monster type and health remain stable across repeated frame updates");
            check(kinds.Count == 5 && AdventureMonster.All.All(type => kinds.Contains(type.Id)), "random spawning can produce all five monster species");
            AdventureBattle delayed = new AdventureBattle(new AdventureProgress());
            AdventureEnemy delayedEnemy = new AdventureEnemy { Id = "delayed", Health = 40, MaxHealth = 40 };
            delayed.Enemies.Add(delayedEnemy); delayed.Authorize(delayedEnemy); delayed.Strike(delayedEnemy);
            check(delayedEnemy.Health == 16 && delayedEnemy.HitFlash > 0 && delayedEnemy.VisualDirty && !delayedEnemy.VisualDefeated && !delayedEnemy.Cleaning, "nonlethal hit gives immediate feedback without scheduling deletion");
            delayed.Cooldown = 0; delayed.Strike(delayedEnemy);
            check(delayedEnemy.VisualDefeated && delayedEnemy.DeathElapsed == 0 && delayedEnemy.Cleaning && !delayedEnemy.Defeated, "lethal hit starts visual shatter before asynchronous cleanup succeeds");
            delayed.Tick(.31);
            check(delayedEnemy.IsVisuallyHidden && delayedEnemy.Authorized && delayedEnemy.Cleaning && !delayedEnemy.Defeated && delayed.Progress.FreedBytes == 0 && delayed.Progress.Experience == delayedEnemy.KillExperience, "delayed cleanup keeps the defeated sprite hidden with kill XP paid and disk bytes still unsettled");
            delayed.Complete(delayedEnemy, 0, 0, "file is locked", 0);
            check(delayedEnemy.VisualDefeated && delayedEnemy.IsVisuallyHidden && delayedEnemy.VisualDirty && delayedEnemy.Health == 0 && delayedEnemy.Defeated && delayedEnemy.Failed && !delayedEnemy.Cleaning, "failed background cleanup cannot revive or replay a defeated sprite");
            AdventureBattle cancelledVisual = new AdventureBattle(new AdventureProgress());
            AdventureEnemy cancelledEnemy = new AdventureEnemy { Id = "cancel-visual", Health = 20, MaxHealth = 20 };
            cancelledVisual.Enemies.Add(cancelledEnemy); cancelledVisual.Authorize(cancelledEnemy); cancelledVisual.Strike(cancelledEnemy); cancelledVisual.Tick(.31); cancelledVisual.CancelQueue();
            check(cancelledEnemy.IsVisuallyHidden && cancelledEnemy.VisualDefeated && !cancelledEnemy.Cleaning && !cancelledEnemy.Authorized && cancelledEnemy.Health == 0 && cancelledEnemy.Defeated, "canceling an unstarted lethal batch keeps its monster dead without deleting files");
            cancelledEnemy = new AdventureEnemy { Id = "cancel-active", Health = 20, MaxHealth = 20 }; cancelledVisual.Enemies.Add(cancelledEnemy);
            cancelledVisual.Authorize(cancelledEnemy); cancelledVisual.Cooldown = 0; cancelledVisual.Strike(cancelledEnemy); cancelledVisual.Tick(.31); cancelledEnemy.CleanupStarted = true; cancelledVisual.CancelQueue();
            check(cancelledEnemy.IsVisuallyHidden && cancelledEnemy.Authorized && cancelledEnemy.Cleaning, "active cleanup retains accounting authority while its sprite remains hidden");
            cancelledVisual.Complete(cancelledEnemy, 0, 0, "cancelled", 1, true);
            check(cancelledEnemy.IsVisuallyHidden && cancelledEnemy.Failed && cancelledEnemy.Defeated && cancelledVisual.LastReward.Contains("取消") && cancelledVisual.Progress.FreedBytes == 0 && cancelledVisual.Progress.Experience >= 4 && cancelledVisual.Progress.Experience <= 6, "zero-success worker cancellation retains both combat XP and permanent death while explaining no released space");
            AdventureBattle delayedEmpty = new AdventureBattle(new AdventureProgress()); AdventureEnemy onlyEmpty = new AdventureEnemy { Id = "delayed-empty", Health = 1, MaxHealth = 1 };
            delayedEmpty.Enemies.Add(onlyEmpty); delayedEmpty.Authorize(onlyEmpty); delayedEmpty.Strike(onlyEmpty); delayedEmpty.Tick(.31); delayedEmpty.Complete(onlyEmpty, 0, 2, "ok", 0);
            check(onlyEmpty.Defeated && onlyEmpty.IsVisuallyHidden && onlyEmpty.DeathElapsed >= .31 && delayedEmpty.Progress.Experience == onlyEmpty.KillExperience, "delayed zero-byte success keeps sprite hidden without replaying death or duplicating kill XP");
            AdventureEnemy patroller = new AdventureEnemy(); Random patrolRandom = new Random(49); HashSet<int> sectors = new HashSet<int>(); bool bounded = true; double minX = Double.MaxValue, maxX = Double.MinValue, minY = Double.MaxValue, maxY = Double.MinValue;
            for (int i = 0; i < 9; i++)
            {
                AdventureEnemy.ChoosePatrolTarget(patroller, patrolRandom, -1400, 100, 1200, 800); sectors.Add(patroller.PatrolSector);
                bounded &= patroller.TargetX >= -1400 && patroller.TargetX <= -200 && patroller.TargetY >= 100 && patroller.TargetY <= 900;
                minX = Math.Min(minX, patroller.TargetX); maxX = Math.Max(maxX, patroller.TargetX); minY = Math.Min(minY, patroller.TargetY); maxY = Math.Max(maxY, patroller.TargetY);
            }
            check(bounded && sectors.Count == 9 && maxX - minX > 600 && maxY - minY > 400, "patrol destinations cover every screen sector within bounds instead of clustering in one corner");
            AdventureEnemy.ChoosePatrolTarget(patroller, patrolRandom, -10, 30, 0, 0);
            check(patroller.TargetX == -10 && patroller.TargetY == 30 && AdventureMonster.All.All(type => type.WalkSpeed > 40 && type.WalkSpeed < 200), "tiny work areas remain bounded and all monster patrol speeds remain catchable");
            AdventureBattle diskWarningBattle = new AdventureBattle(new AdventureProgress());
            AdventureEnemy diskWarning = new AdventureEnemy { Id = "disk-warning", IsBoss = true, IsDiskBoss = true, Health = 700, MaxHealth = 700 };
            AdventureEnemy diskEmptyWarning = new AdventureEnemy { Id = "disk-empty-warning", IsBoss = true, IsDiskBoss = true, Batch = emptyBatch, Health = 700, MaxHealth = 700 };
            diskWarningBattle.Enemies.AddRange(new[] { diskWarning, diskEmptyWarning });
            check(!diskWarningBattle.Authorize(diskWarning) && !diskWarningBattle.Authorize(diskEmptyWarning) && diskWarningBattle.QueueCount == 0 && diskWarningBattle.LastReward.Contains("没有可安全清理"), "disk warning with no safe files cannot be manually authorized and explains why");
            diskWarningBattle.SetAutomatic(true); check(diskWarningBattle.PlanNext(0, 0) == null && !diskWarning.Authorized && !diskEmptyWarning.Authorized, "automatic planning excludes disk warnings without cleanup payloads");
            check(diskWarningBattle.ClearFieldSnapshot().Count == 0 && diskWarning.Health == 700 && !diskWarning.VisualDefeated, "one-click clear never attacks an unbacked disk warning");
            diskWarning.Cleaning = diskWarning.Authorized = true; diskWarningBattle.Complete(diskWarning, 4096, 1, "invalid unbacked callback", 0);
            check(!diskWarning.Defeated && !diskWarning.Authorized && diskWarningBattle.Progress.Score == 0 && diskWarningBattle.Progress.Experience == 0 && diskWarningBattle.Progress.FreedBytes == 0, "an unbacked disk warning cannot accept a fabricated success callback");
            CleanupBatch diskBatch = new CleanupBatch(new List<CleanupCandidate> { new CleanupCandidate { Path = "C:\\KnightAssistant-synthetic-only\\disk.bin", Length = 4096 } }, 1);
            CleanupBatch emptyDiskFileBatch = new CleanupBatch(new List<CleanupCandidate> { new CleanupCandidate { Path = "C:\\KnightAssistant-synthetic-only\\empty.bin", Length = 0 } }, 2);
            Func<string, CleanupBatch, AdventureEnemy> makeDiskBoss = delegate(string id, CleanupBatch batch) { return new AdventureEnemy { Id = id, IsBoss = true, IsDiskBoss = true, Batch = batch, Health = 10, MaxHealth = 10 }; };
            AdventureBattle diskFailureBattle = new AdventureBattle(new AdventureProgress()); AdventureEnemy diskFailure = makeDiskBoss("disk-failure", diskBatch);
            diskFailureBattle.Enemies.Add(diskFailure); diskFailureBattle.Authorize(diskFailure); diskFailureBattle.Strike(diskFailure); diskFailureBattle.Complete(diskFailure, 0, 0, "locked", 0);
            check(diskFailure.Failed && diskFailure.Defeated && diskFailure.Health == 0 && diskFailureBattle.Progress.Score == diskFailure.KillExperience * 5 && diskFailureBattle.Progress.Experience == diskFailure.KillExperience, "disk boss failure preserves only its kill XP and never takes the memory-boss bonus path");
            AdventureBattle diskEmptyBattle = new AdventureBattle(new AdventureProgress()); AdventureEnemy diskEmpty = makeDiskBoss("disk-zero-byte", emptyDiskFileBatch);
            diskEmptyBattle.Enemies.Add(diskEmpty); diskEmptyBattle.Authorize(diskEmpty); diskEmptyBattle.Strike(diskEmpty); diskEmptyBattle.Complete(diskEmpty, 0, 1, "deleted empty file", 0);
            check(diskEmpty.Defeated && diskEmptyBattle.Progress.Score == diskEmpty.KillExperience * 5 && diskEmptyBattle.Progress.Experience == diskEmpty.KillExperience && diskEmptyBattle.Progress.FreedBytes == 0 && diskEmptyBattle.Progress.Inventory.Count == 1, "zero-byte disk boss success preserves kill XP but adds no cleanup XP, bonus, equipment or invented space");
            AdventureBattle diskSuccessBattle = new AdventureBattle(new AdventureProgress()); AdventureEnemy diskSuccess = makeDiskBoss("disk-success", diskBatch);
            diskSuccessBattle.Enemies.Add(diskSuccess); diskSuccessBattle.Authorize(diskSuccess); diskSuccessBattle.Strike(diskSuccess);
            check(diskSuccess.Cleaning && !diskSuccess.Defeated && diskSuccessBattle.Progress.Score == diskSuccess.KillExperience * 5 && diskSuccessBattle.Progress.FreedBytes == 0, "disk boss lethal damage grants kill XP while disk accounting and bonus wait for actual results");
            diskSuccessBattle.Complete(diskSuccess, 4096, 1, "deleted", 1); string diskSuccessNotice = diskSuccessBattle.LastReward;
            check(diskSuccess.Defeated && diskSuccessBattle.Progress.FreedBytes == 4096 && diskSuccessBattle.Progress.Experience == diskSuccess.KillExperience && diskSuccessBattle.Progress.Score == diskSuccess.KillExperience * 5 + 100 && diskSuccessNotice.Contains("磁盘危机"), "real disk boss cleanup records actual bytes and one combat bonus without duplicating kill XP");
            diskSuccessBattle.Complete(diskSuccess, 4096, 1, "duplicate", 0);
            check(diskSuccessBattle.Progress.FreedBytes == 4096 && diskSuccessBattle.Progress.Experience == diskSuccess.KillExperience && diskSuccessBattle.Progress.Score == diskSuccess.KillExperience * 5 + 100 && diskSuccessBattle.Progress.Inventory.Count == 1 && diskSuccessBattle.LastReward == diskSuccessNotice, "disk boss bonus and cleanup rewards are idempotent");
            AdventureBattle diskCancelBattle = new AdventureBattle(new AdventureProgress()); AdventureEnemy diskCancel = makeDiskBoss("disk-cancel", diskBatch);
            diskCancelBattle.Enemies.Add(diskCancel); diskCancelBattle.Authorize(diskCancel); diskCancelBattle.Strike(diskCancel); diskCancelBattle.Tick(.31); diskCancelBattle.Complete(diskCancel, 0, 0, "cancelled", 0, true);
            check(diskCancel.Failed && diskCancel.Defeated && diskCancel.IsVisuallyHidden && diskCancelBattle.Progress.Score == diskCancel.KillExperience * 5 && diskCancelBattle.Progress.FreedBytes == 0 && diskCancelBattle.Progress.Experience == diskCancel.KillExperience, "cancelled disk cleanup keeps the boss dead and its kill XP without a fake disk bonus");
            CleanupBatch partialDiskBatch = new CleanupBatch(new List<CleanupCandidate> { new CleanupCandidate { Path = "C:\\KnightAssistant-synthetic-only\\partial-a.bin", Length = 2048 }, new CleanupCandidate { Path = "C:\\KnightAssistant-synthetic-only\\partial-b.bin", Length = 2048 } }, 3);
            AdventureBattle partialDiskBattle = new AdventureBattle(new AdventureProgress()); AdventureEnemy partialDisk = makeDiskBoss("partial-disk", partialDiskBatch);
            partialDiskBattle.Enemies.Add(partialDisk); partialDiskBattle.Authorize(partialDisk); partialDiskBattle.Strike(partialDisk); partialDiskBattle.Complete(partialDisk, 2048, 1, "one deleted then cancelled", 1, true);
            check(partialDisk.Defeated && partialDiskBattle.Progress.FreedBytes == 2048 && partialDiskBattle.Progress.Score == partialDisk.KillExperience * 5 + 100 && partialDiskBattle.Progress.Experience == partialDisk.KillExperience && partialDiskBattle.LastReward.Contains("剩余文件已取消"), "partially cancelled disk cleanup accounts only successfully deleted bytes and explains the remaining files");
            AdventureBattle inconsistentDiskBattle = new AdventureBattle(new AdventureProgress()); AdventureEnemy inconsistentDisk = makeDiskBoss("inconsistent-disk", diskBatch);
            inconsistentDiskBattle.Enemies.Add(inconsistentDisk); inconsistentDiskBattle.Authorize(inconsistentDisk); inconsistentDiskBattle.Strike(inconsistentDisk); inconsistentDiskBattle.Complete(inconsistentDisk, 4096, 0, "no deleted files", 0);
            check(inconsistentDisk.Failed && inconsistentDiskBattle.Progress.FreedBytes == 0 && inconsistentDiskBattle.Progress.Score == inconsistentDisk.KillExperience * 5 && inconsistentDiskBattle.Progress.Experience == inconsistentDisk.KillExperience, "disk boss cannot award claimed bytes or bonus when no files were actually deleted");
            AdventureBattle simultaneous = new AdventureBattle(new AdventureProgress()); simultaneous.Enemies.Add(makeDiskBoss("existing-disk", diskBatch));
            bool memoryAlongsideDisk = false; for (int i = 0; i < 3; i++) memoryAlongsideDisk |= simultaneous.ObserveMemory(90, 5, 200 + i * 5);
            check(memoryAlongsideDisk, "existing disk boss does not suppress an independent RAM pressure boss");
            AdventureBattle duplicateMemory = new AdventureBattle(new AdventureProgress()); duplicateMemory.Enemies.Add(new AdventureEnemy { IsBoss = true, IsDiskBoss = false });
            bool duplicateMemorySpawn = false; for (int i = 0; i < 3; i++) duplicateMemorySpawn |= duplicateMemory.ObserveMemory(90, 5, 200 + i * 5);
            check(!duplicateMemorySpawn, "an existing memory boss still suppresses duplicate memory-boss spawns");
            AdventureBattle earlyFailure = new AdventureBattle(new AdventureProgress(), new Random(29));
            AdventureEnemy earlyEnemy = new AdventureEnemy { Id = "early-fail", Health = 1, MaxHealth = 1 }; earlyFailure.Enemies.Add(earlyEnemy); earlyFailure.Authorize(earlyEnemy); earlyFailure.Strike(earlyEnemy); earlyFailure.Tick(.15);
            int earnedBeforeFailure = earlyFailure.Progress.Experience;
            earlyFailure.Complete(earlyEnemy, 0, 0, "busy", 0);
            check(earlyEnemy.Defeated && earlyEnemy.Failed && earlyEnemy.Health == 0 && !earlyEnemy.IsVisuallyHidden && earlyEnemy.DeathElapsed == .15 && earlyFailure.Progress.Experience == earnedBeforeFailure, "failure during shatter preserves elapsed animation and kill XP");
            earlyFailure.Tick(.16); earlyEnemy.RestoreVisual("stale visual callback");
            check(earlyEnemy.IsVisuallyHidden && earlyEnemy.Health == 0 && earlyEnemy.DeathElapsed >= .31, "even a stale restore callback cannot resurrect a fully shattered monster");
            string failedNotice = earlyFailure.LastReward; earlyFailure.Complete(earlyEnemy, 99999, 1, "late duplicate", 0);
            check(earlyFailure.Progress.Experience == earnedBeforeFailure && earlyFailure.Progress.FreedBytes == 0 && earlyFailure.LastReward == failedNotice && !earlyFailure.Authorize(earlyEnemy), "failed completion is final and ignores a duplicate success callback or reauthorization");
            AdventureBattle cancelAlive = new AdventureBattle(new AdventureProgress(), new Random(1));
            AdventureEnemy living = new AdventureEnemy { Id = "living", Health = 80, MaxHealth = 80 }; cancelAlive.Enemies.Add(living); cancelAlive.Authorize(living); cancelAlive.Strike(living); cancelAlive.CancelQueue();
            check(living.Health == 56 && !living.Defeated && !living.VisualDefeated && !living.KillRewarded && !living.Authorized && cancelAlive.Progress.Experience == 0, "cancelling a nonlethal attack keeps the wounded monster alive and awards no XP");
            AdventureBattle cancelledOnce = new AdventureBattle(new AdventureProgress(), new Random(2));
            AdventureEnemy cancelAfterHit = new AdventureEnemy { Id = "cancel-twice", Health = 1, MaxHealth = 1 }; cancelledOnce.Enemies.Add(cancelAfterHit); cancelledOnce.Authorize(cancelAfterHit); cancelledOnce.Strike(cancelAfterHit); cancelledOnce.Tick(.12);
            int cancellationXP = cancelledOnce.Progress.Experience; cancelledOnce.CancelQueue();
            check(cancelAfterHit.Defeated && cancelAfterHit.Health == 0 && cancelAfterHit.DeathElapsed == .12 && cancelledOnce.Progress.Experience == cancellationXP && cancelledOnce.Progress.FreedBytes == 0, "lethal cancellation preserves prior XP and the original shatter timer");
            check(!cancelAfterHit.CancelBeforeCleanup("duplicate cancel") && !cancelledOnce.GrantKillExperience(cancelAfterHit) && cancelledOnce.Progress.Experience == cancellationXP, "repeated cancellation cannot retire the same target twice or grant duplicate XP");
            AdventureEnemy noCombat = new AdventureEnemy { Authorized = true, Health = 0 }; check(!cancelledOnce.GrantKillExperience(noCombat), "zero health without a combat shatter cannot fabricate a kill reward");
            AdventureBattle killSequence = new AdventureBattle(new AdventureProgress(), new Random(714)); Random expectedKills = new Random(714); bool deterministicKills = true, noExtraRewards = true; int sequenceXP = 0; HashSet<int> observedXP = new HashSet<int>();
            for (int i = 0; i < 16; i++)
            {
                AdventureEnemy foe = new AdventureEnemy { Id = "xp-" + i, Health = 1, MaxHealth = 1 }; killSequence.Enemies.Add(foe); killSequence.Authorize(foe); killSequence.Cooldown = 0; killSequence.Strike(foe);
                int expectedXP = expectedKills.Next(2, 4); sequenceXP += expectedXP; observedXP.Add(foe.KillExperience);
                deterministicKills &= foe.KillRewarded && foe.KillExperience == expectedXP && killSequence.Progress.Experience == sequenceXP;
                int beforeScore = killSequence.Progress.Score; noExtraRewards &= !killSequence.GrantKillExperience(foe);
                killSequence.Complete(foe, i % 2 == 0 ? 1024 : 0, i % 2 == 0 ? 1 : 0, "synthetic", 1);
                noExtraRewards &= killSequence.Progress.Experience == sequenceXP && killSequence.Progress.Score == beforeScore;
            }
            check(deterministicKills && observedXP.SetEquals(new[] { 2, 3 }), "seeded kill RNG awards both two and three XP exactly once per lethal strike");
            check(noExtraRewards && killSequence.Progress.FreedBytes == 8 * 1024, "repeated award requests and mixed cleanup success/failure cannot duplicate kill XP or inflate actual bytes");
            AdventureBattle levelKill = new AdventureBattle(new AdventureProgress { Experience = 59 }, new Random(17)); AdventureEnemy levelTarget = new AdventureEnemy { Health = 1, MaxHealth = 1 };
            levelKill.Enemies.Add(levelTarget); levelKill.Authorize(levelTarget); levelKill.Strike(levelTarget);
            check(levelKill.Progress.Level == 2 && levelKill.Progress.Experience == levelTarget.KillExperience - 1 && levelKill.Progress.Inventory.Count == 2 && levelKill.LevelGlow > 0 && levelKill.Progress.FreedBytes == 0, "lethal kill grants level-up glow and unlock before any cleanup result");
            int unlockedWeapons = levelKill.Progress.Inventory.Count; levelKill.Complete(levelTarget, 0, 0, "failed", 0);
            check(levelKill.Progress.Level == 2 && levelKill.Progress.Inventory.Count == unlockedWeapons, "cleanup failure cannot roll back a legitimately earned level or unlock");
            AdventureBattle deathWork = new AdventureBattle(new AdventureProgress(), new Random(41));
            AdventureEnemy waitingDead = new AdventureEnemy { Id = "death-waiting", Health = 1, MaxHealth = 1 }, activeDead = new AdventureEnemy { Id = "death-active", Health = 1, MaxHealth = 1 }, untouched = new AdventureEnemy { Id = "death-alive", Health = 80, MaxHealth = 80 };
            deathWork.Enemies.AddRange(new[] { waitingDead, activeDead, untouched }); deathWork.Authorize(waitingDead); deathWork.Authorize(activeDead); deathWork.Authorize(untouched);
            deathWork.Strike(waitingDead); deathWork.Cooldown = 0; deathWork.Strike(activeDead); activeDead.CleanupStarted = true; deathWork.Automatic = true; deathWork.Damage(9999); deathWork.StopFightingAfterDeath();
            check(!deathWork.Automatic && waitingDead.Authorized && waitingDead.Cleaning && activeDead.Authorized && activeDead.CleanupStarted && deathWork.QueueCount == 2 && !untouched.Authorized && !untouched.Defeated && untouched.Health == 80, "hero death stops attacks but retains both queued lethal cleanup and running-worker authority");
            int deathXP = deathWork.Progress.Experience;
            check(!deathWork.Authorize(untouched) && deathWork.ClearFieldSnapshot().Count == 0 && deathWork.PlanNext(0, 0) == null, "dead hero cannot authorize future attacks or expand its cleanup snapshot");
            deathWork.Complete(waitingDead, 2048, 1, "actual synthetic result", 1); deathWork.Complete(activeDead, 0, 0, "cancelled", 1, true); deathWork.Tick(.31);
            check(deathWork.Progress.Dead && deathWork.Progress.Health == 0 && deathWork.Progress.Experience == deathXP && deathWork.Progress.FreedBytes == 2048 && deathWork.QueueCount == 0 && waitingDead.IsVisuallyHidden && activeDead.IsVisuallyHidden, "post-death queued cleanup settles real bytes once while keeping hero and monsters dead");
            string fixtureDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistantProject", "v5.2", "model-selftest", Guid.NewGuid().ToString("N"));
            string temporary = Path.Combine(fixtureDirectory, "progress.txt");
            try { p.Inventory.Add("laser"); p.WeaponId = "laser"; p.ThemeMode = "JP"; p.Save(temporary); AdventureProgress loaded = AdventureProgress.Load(temporary); check(loaded.WeaponId == "laser" && loaded.FreedBytes == p.FreedBytes && loaded.ThemeMode == "JP", "isolated progress roundtrip outside user Temp and real save"); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); if (File.Exists(temporary + ".new")) File.Delete(temporary + ".new"); if (Directory.Exists(fixtureDirectory)) Directory.Delete(fixtureDirectory, false); }
            return "Adventure model: " + checks + " assertions passed (synthetic combat plus isolated save fixture; user Temp and real progress untouched).";
        }
    }
}





