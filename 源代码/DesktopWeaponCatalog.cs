using System;
using System.Linq;

namespace NetBunny
{
    // Desktop presentation only. Ownership, rewards and combat numbers still
    // come from AdventureProgress/AdventureWeapon and the existing battle.
    internal sealed class DesktopWeaponInfo
    {
        internal string Id, Name, Role;
        internal string ImageFile { get { return ArtDemoPack.WeaponAsset(Id) + ".png"; } }
    }
    internal static class DesktopWeaponCatalog
    {
        static readonly DesktopWeaponInfo[] catalog = {
            new DesktopWeaponInfo { Id = "spear", Name = "灵铁短剑", Role = "近身突刺" },
            new DesktopWeaponInfo { Id = "greatsword", Name = "青钢重剑", Role = "近身重斩" },
            new DesktopWeaponInfo { Id = "wave", Name = "破空剑气", Role = "远距剑气" },
            new DesktopWeaponInfo { Id = "laser", Name = "流光飞剑", Role = "快速御剑" },
            new DesktopWeaponInfo { Id = "boomerang", Name = "月轮飞刃", Role = "月轮回斩" },
            new DesktopWeaponInfo { Id = "blackhole", Name = "归墟剑阵", Role = "远距剑阵" },
            new DesktopWeaponInfo { Id = "automaton", Name = "御剑符", Role = "御符连击" }
        };
        internal static DesktopWeaponInfo Find(string id)
        { return catalog.FirstOrDefault(w => w.Id == id) ?? catalog[0]; }
        internal static string ProgressText(AdventureProgress progress)
        {
            AdventureWeapon next = AdventureWeapon.All.FirstOrDefault(w => !progress.Inventory.Contains(w.Id));
            string text = "已拥有 " + AdventureWeapon.All.Count(w => progress.Inventory.Contains(w.Id)) + "/" + AdventureWeapon.All.Length
                + " 种兵器 · 当前装备「" + Find(progress.WeaponId).Name + "」";
            return text + (next == null ? "\n兵器已收齐，可按出招方式自由选择。"
                : "\n下次历练升级可获「" + Find(next.Id).Name + "」；还需 " + Math.Max(0, progress.NextLevelExperience - progress.Experience) + " 历练经验。");
        }
    }
}
