using System;

namespace NetBunny
{
    internal static class EnemyBalance
    {
        internal static int OrdinaryHealth(int level, double factor, long bytes)
        {
            level = Math.Max(1, Math.Min(100,level));
            return Math.Max(100, (int)Math.Ceiling((150 + (level-1)*24 + Math.Min(120, Math.Max(0,bytes)/(8L*1024*1024))) * factor));
        }
        internal static int MemoryBossHealth(int level, AdventureWeapon weapon)
        {
            // Normal automatic combat target: ~120 seconds, including melee retreat/re-entry.
            double interval = weapon.Cooldown + (weapon.Range <= 160 ? .12 : 0);
            return (int)Math.Ceiling(120 / interval) * (weapon.Damage + (Math.Max(1, Math.Min(100,level))-1)*3);
        }
        internal static AdventureEnemy MemoryBoss(AdventureProgress progress, AdventureWeapon playerWeapon, Random random)
        {
            int hp = MemoryBossHealth(progress.Level,playerWeapon);
            return new AdventureEnemy { Id = "memory-" + Guid.NewGuid().ToString("N"), IsBoss = true, MonsterKind = "boss", EvilTheme = RasterActorRenderer.ThemeCodes[random.Next(6)], BossWeapon = AdventureWeapon.All[random.Next(AdventureWeapon.All.Length)].Id, Health = hp, MaxHealth = hp, SpawnLevel = progress.Level };
        }
        internal static string SelfTest()
        {
            int n=0;
            foreach (AdventureWeapon weapon in AdventureWeapon.All)
            foreach (int level in new[]{1,10,50,100})
            {
                double expected = Math.Ceiling(MemoryBossHealth(level,weapon)/(double)(weapon.Damage+(level-1)*3))*(weapon.Cooldown+(weapon.Range<=160?.12:0));
                if (expected<120 || expected>122) throw new Exception("Boss target duration"); n++;
                if (OrdinaryHealth(level+1,1,0) < OrdinaryHealth(level,1,0)) throw new Exception("Enemy HP must grow with hero"); n++;
            }
            return "Enemy balance: " + n + " checks passed (120-second nominal normal-combat target).";
        }
    }
}
