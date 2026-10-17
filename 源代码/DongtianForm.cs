using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace NetBunny
{
    // P4 洞天经营面板：功能优先的最小界面——状态总览 + 操作按钮流，
    // 视觉结构先行，完整美术升级属后续纵切。
    internal sealed class DongtianForm : Form
    {
        private readonly AdventureController controller;
        private readonly Label status = new Label(), notice = new Label();
        private readonly FlowLayoutPanel actions;
        private readonly ToolTip hints = new ToolTip();

        internal DongtianForm(AdventureController controller)
        {
            this.controller = controller;
            Text = "洞天经营 · 炼丹炼器";
            Width = 720; Height = 560; StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(250, 247, 240); Font = new Font("Microsoft YaHei UI", 10F);
            FlowLayoutPanel content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(20), BackColor = BackColor };
            Label title = new Label { Text = "洞天经营", AutoSize = true, Font = new Font(Font.FontFamily, 17, FontStyle.Bold), ForeColor = Color.FromArgb(73, 49, 132), Margin = new Padding(0, 0, 0, 8) };
            status.AutoSize = false; status.Width = 640; status.Height = 300; status.Margin = new Padding(0, 2, 0, 6);
            notice.AutoSize = false; notice.Width = 640; notice.Height = 34; notice.ForeColor = Color.FromArgb(130, 84, 32);
            actions = new FlowLayoutPanel { Width = 650, AutoSize = true, WrapContents = true };
            content.Controls.Add(title); content.Controls.Add(status); content.Controls.Add(notice); content.Controls.Add(actions);
            Controls.Add(content);
        }

        private Button Action(string label, int width, string tip, Func<bool> run)
        {
            Button button = new Button { Text = label, Width = width, Height = 34, Margin = new Padding(0, 3, 8, 3) };
            if (!String.IsNullOrEmpty(tip)) hints.SetToolTip(button, tip);
            button.Click += delegate { if (run != null && run()) { } notice.Text = controller.Dongtian == null ? "" : controller.Dongtian.LastNotice; RefreshData(); };
            return button;
        }

        internal void RefreshData()
        {
            DongtianProgress d = controller.Dongtian;
            actions.Controls.Clear();
            if (d == null) { status.Text = "洞天数据不可用。"; return; }
            DateTime utc = DateTime.UtcNow;
            if (!controller.DongtianUnlocked)
            {
                status.Text = "洞天内苑（预览）\r\n\r\n筑基之后，方可开辟洞天、开垦灵田、筑炉炼丹、冶器养灵。\r\n"
                    + "蜃楼带回的水月灵髓届时可入库入药——每一次秘境之行都在为洞天积攒家底。\r\n\r\n当前境界："
                    + controller.Cultivation.RealmName + " · 第" + controller.Cultivation.Layer + "层";
                notice.Text = "";
                return;
            }
            StringBuilder text = new StringBuilder();
            text.AppendLine("洞天等级 Lv." + d.Level + " · 灵田 " + d.UnlockedFields + " 畦 · 丹炉 1 座 · 器位 1 处");
            text.AppendLine();
            for (int i = 0; i < d.UnlockedFields; i++)
            {
                string slot = d.Fields[i];
                if (slot == null) { text.AppendLine("灵田·" + (i + 1) + "：空置"); continue; }
                string recipe; long ready;
                if (DongtianField(slot, out recipe, out ready))
                {
                    DongtianSeedDef seed = DongtianRecipes.Seed(recipe);
                    double remainHours = (ready - utc.Ticks) / (double)TimeSpan.TicksPerHour;
                    text.AppendLine("灵田·" + (i + 1) + "：" + (seed == null ? recipe : seed.Name) + (remainHours <= 0 ? " · 已成熟，可收获" : " · 生长中，约 " + Math.Max(0.1, remainHours).ToString("F1") + " 小时后成熟"));
                }
            }
            text.AppendLine("丹炉：" + SlotText(d.Furnace, "丹", utc));
            text.AppendLine("器位：" + SlotText(d.Forge, "器", utc));
            text.AppendLine();
            StringBuilder bag = new StringBuilder("洞天储物：");
            foreach (System.Collections.Generic.KeyValuePair<string, int> pair in d.Bag) bag.Append(DongtianRecipes.MaterialName(pair.Key) + "×" + pair.Value + "　");
            if (d.Bag.Count == 0) bag.Append("（空）——灵田收获或灵域入库后入库在册");
            text.AppendLine(bag.ToString());
            StringBuilder active = new StringBuilder("生效增益：");
            if (d.BuffActive("buff-juqi", utc)) active.Append("聚气散(吐纳+25%)　");
            if (d.BuffActive("buff-wuzhen", utc)) active.Append("悟真丹(吐纳+15%)　");
            if (d.BuffActive("buff-gushen", utc)) active.Append("固神丹(调息减半)　");
            if (d.BuffActive("buff-ninglu", utc)) active.Append("凝露丹(伤害+8%)　");
            foreach (string gear in d.Gears) { DongtianGearDef def = DongtianRecipes.Gear(gear); if (def != null) active.Append(def.Name + "　"); }
            if (active.Length == 5) active.Append("（暂无）");
            text.AppendLine(active.ToString());
            status.Text = text.ToString();
            notice.Text = d.LastNotice;
            // 操作按钮
            foreach (DongtianSeedDef seed in DongtianRecipes.Seeds)
                for (int i = 0; i < d.UnlockedFields; i++)
                    if (d.Fields[i] == null) { int captured = i; actions.Controls.Add(Action("种" + seed.Name + "→田" + (i + 1), 150, seed.GrowSeconds / 3600 + " 小时成熟 · 收获 ×" + seed.Yield, delegate { return controller.DongtianPlant(captured, seed.Id); })); break; }
            for (int i = 0; i < d.UnlockedFields; i++)
                if (d.FieldReady(i, utc)) { int captured = i; actions.Controls.Add(Action("收获田" + (i + 1), 110, "成熟作物收进洞天储物", delegate { return controller.DongtianHarvest(captured); })); }
            int lingsui = controller.LingyuMaterialCount("shuiyue-lingsui");
            if (lingsui > 0) actions.Controls.Add(Action("灵髓入库（灵域剩 " + lingsui + "）", 190, "把水月灵髓从灵域收藏带回洞天储物；双向台账幂等入账", delegate { return controller.DongtianImport("shuiyue-lingsui"); }));
            if (d.Furnace == null)
                foreach (DongtianPillDef pill in DongtianRecipes.Pills)
                {
                    string tip = pill.EffectText + " · 材料：" + MaterialsText(pill.Materials);
                    actions.Controls.Add(Action("炼" + pill.Name, 120, tip, delegate { return controller.DongtianStartPill(pill.Id); }));
                }
            else if (d.FurnaceReady(utc)) actions.Controls.Add(Action("收丹", 90, "丹成即服，限时增益立即生效", delegate { return controller.DongtianCollectPill(); }));
            if (d.Forge == null)
                foreach (DongtianGearDef gear in DongtianRecipes.Gears)
                    if (!d.HasGear(gear.Id))
                        actions.Controls.Add(Action("铸" + gear.Name, 110, gear.EffectText + " · 材料：" + MaterialsText(gear.Materials), delegate { return controller.DongtianStartGear(gear.Id); }));
            else if (d.ForgeReady(utc)) actions.Controls.Add(Action("收器", 90, "器灵铸成，常驻生效", delegate { return controller.DongtianCollectGear(); }));
        }
        private static bool DongtianField(string slot, out string recipe, out long ready)
        {
            recipe = ""; ready = 0;
            int comma = slot == null ? -1 : slot.IndexOf(',');
            if (comma < 1) return false;
            recipe = slot.Substring(0, comma);
            return Int64.TryParse(slot.Substring(comma + 1), out ready);
        }
        private string SlotText(string slot, string kind, DateTime utc)
        {
            if (slot == null) return "空闲";
            string recipe; long ready;
            if (!DongtianField(slot, out recipe, out ready)) return "空闲";
            double remainMin = (ready - utc.Ticks) / (double)TimeSpan.TicksPerMinute;
            return recipe + (remainMin <= 0 ? " · 已成，可收" : " · 炼制中，约 " + Math.Max(1, (int)remainMin) + " 分钟");
        }
        private static string MaterialsText(string[] materials)
        {
            System.Collections.Generic.Dictionary<string, int> need = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string m in materials) need[m] = (need.ContainsKey(m) ? need[m] : 0) + 1;
            StringBuilder text = new StringBuilder();
            foreach (System.Collections.Generic.KeyValuePair<string, int> pair in need) text.Append(DongtianRecipes.MaterialName(pair.Key) + "×" + pair.Value + " ");
            return text.ToString().Trim();
        }
    }
}
