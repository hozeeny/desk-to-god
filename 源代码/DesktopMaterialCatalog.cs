using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NetBunny
{
    // Detached descriptions of the inventories and consumers already implemented
    // by the game. Reading or navigating these rows never transfers an item.
    internal sealed class DesktopMaterialInfo
    {
        internal string Source, Id, Name, Location, Purpose, Image, ActionId, ActionText;
        internal int Count;
        internal string Key { get { return Source + "-" + Id; } }
    }
    internal static class DesktopMaterialCatalog
    {
        internal static DesktopMaterialInfo[] Read(AdventureController game)
        {
            var rows = new List<DesktopMaterialInfo>();
            foreach (var pair in game.Lingyu.Materials.Where(p => p.Value > 0)
                .OrderBy(p => p.Key == "shuiyue-lingsui" ? 0 : p.Key == "jinghua-shuiyue" ? 1 : 2).ThenBy(p => p.Key, StringComparer.Ordinal))
            {
                string id = pair.Key;
                var row = Item("lingyu", id, pair.Value, LingyuCatalog.MaterialName(id), "灵域收藏 · 尚未入库", "item-material.png");
                bool importable = id == "shuiyue-lingsui" || id == "jinghua-shuiyue";
                if (importable)
                {
                    row.Purpose = "筑基后可在洞天每次转入1份；入库会转出灵域收藏，没有退库入口。\n"
                        + "洞天仓储现有「" + row.Name + "」×" + game.Dongtian.CountOf(id) + "。\n" + CaveUses(id, game.Dongtian);
                    if (id == "shuiyue-lingsui")
                    {
                        row.Image = "item-marrow.png";
                        row.Purpose = "入库后不计入破境材料。可先留在灵域收藏，或筑基后转作炼丹材料。\n" + row.Purpose;
                    }
                    CultivationRealmDef realm = game.Cultivation.RealmDef;
                    if (!game.Cultivation.Mortal && realm.MaterialId == id)
                    {
                        row.Purpose = "本境圆满并获得契机后，可选用×" + Math.Max(1, realm.MaterialCount) + "辅助破境，成功率+"
                            + realm.MaterialBonusPercent + "%（成败皆消耗）。\n" + row.Purpose;
                        row.ActionId = "breakthrough"; row.ActionText = "查看破境 · 选择是否使用";
                    }
                    else
                    {
                        row.ActionId = "dongtian";
                        row.ActionText = game.DongtianUnlocked ? "前往洞天 · 查看入库与配方" : "查看洞天 · 筑基后入库";
                    }
                }
                else CollectionOnly(row);
                rows.Add(row);
            }
            foreach (var pair in game.Dongtian.Bag.Where(p => p.Value > 0).OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var row = Item("dongtian", pair.Key, pair.Value, DongtianRecipes.MaterialName(pair.Key), "洞天仓储 · 已入库", "item-material.png");
                row.Purpose = CaveUses(pair.Key, game.Dongtian);
                if (pair.Key == "shuiyue-lingsui") { row.Image = "item-marrow.png"; row.Purpose = "已从灵域转出，不计入破境材料，也不能退回灵域。\n" + row.Purpose; }
                row.ActionId = "dongtian"; row.ActionText = "查看洞天配方与扩建";
                if (!HasCaveUse(pair.Key, game.Dongtian)) { row.Purpose = "当前没有已开放的配方，暂作收藏保留；不会自动消耗。"; row.ActionId = null; row.ActionText = null; }
                if (game.DongtianPersistPending) row.Purpose = "档案待保存，当前数量尚待落盘；请在洞天查看重试状态。\n" + row.Purpose;
                rows.Add(row);
            }
            foreach (var pair in game.Lingyu.Scrolls.Where(p => p.Value > 0).OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var row = Item("scroll", pair.Key, pair.Value, LingyuCatalog.ScrollName(pair.Key), "灵域残卷收藏", "item-scroll.png");
                CollectionOnly(row); rows.Add(row);
            }
            foreach (string id in game.Lingyu.Heritages.OrderBy(p => p, StringComparer.Ordinal))
            {
                var row = Item("heritage", id, 1, LingyuCatalog.HeritageName(id), "灵域传承收藏", "item-material.png");
                CollectionOnly(row); rows.Add(row);
            }
            return rows.ToArray();
        }
        static DesktopMaterialInfo Item(string source, string id, int count, string name, string location, string image)
        {
            return new DesktopMaterialInfo { Source = source, Id = id, Count = count,
                Name = name == id ? source == "scroll" ? "未识得残卷" : source == "heritage" ? "未识得传承" : "未识得材料" : name,
                Location = location, Image = image };
        }
        static void CollectionOnly(DesktopMaterialInfo row)
        {
            row.Purpose = "目前仅作收藏记录，暂无消耗、学习或装备入口；不会自动产生额外增益。";
            row.ActionId = "lingyu"; row.ActionText = "查看灵域收藏";
        }
        static bool HasCaveUse(string id, DongtianProgress cave)
        {
            DongtianExpansionDef expansion = DongtianRecipes.Expansion(cave.Level);
            return DongtianRecipes.Pills.Any(p => p.Materials.Contains(id)) || DongtianRecipes.Gears.Any(p => !cave.HasGear(p.Id) && p.Materials.Contains(id))
                || (expansion != null && expansion.Materials.Contains(id));
        }
        static string Costs(string[] items, DongtianProgress cave)
        {
            return String.Join("、", items.GroupBy(id => id).Select(group => DongtianRecipes.MaterialName(group.Key) + "×" + group.Count()
                + (cave.CountOf(group.Key) < group.Count() ? "（仓储缺" + (group.Count() - cave.CountOf(group.Key)) + "）" : "")).ToArray());
        }
        static string CaveUses(string id, DongtianProgress cave)
        {
            var lines = new List<string>();
            foreach (DongtianPillDef pill in DongtianRecipes.Pills.Where(p => p.Materials.Contains(id)))
                lines.Add(pill.Name + "：" + Costs(pill.Materials, cave) + "；" + pill.EffectText + "。炼制"
                    + (pill.BrewSeconds * cave.FurnaceTimeFactor / 60).ToString("0", CultureInfo.InvariantCulture) + "分钟，收丹即服。");
            foreach (DongtianGearDef gear in DongtianRecipes.Gears.Where(p => !cave.HasGear(p.Id) && p.Materials.Contains(id)))
                lines.Add(gear.Name + "：" + Costs(gear.Materials, cave) + "；" + gear.EffectText + "。炼制"
                    + (gear.ForgeSeconds / 60).ToString(CultureInfo.InvariantCulture) + "分钟，收器后生效。");
            DongtianExpansionDef expansion = DongtianRecipes.Expansion(cave.Level);
            if (expansion != null && expansion.Materials.Contains(id))
                lines.Add("扩建洞天至Lv." + expansion.ToLevel + "：" + Costs(expansion.Materials, cave) + "；新增一畦灵田。");
            return String.Join("\n", lines.ToArray());
        }
    }
}
