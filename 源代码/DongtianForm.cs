using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace NetBunny
{
    // P4 洞天经营面板：功能优先的最小界面——状态总览 + 操作按钮流，
    // 视觉结构先行，完整美术升级属后续纵切。
    // 面板开着时灵田成熟、丹炉完成、器位完成必须自动出现领取操作：
    // 2 秒低频刷新文本；操作按钮按「动作签名」判变，集合不变时复用
    // 既有控件实例，绝不每拍重建（长时间开窗不产生 GDI/USER 泄漏）。
    internal sealed class DongtianForm : Form
    {
        private readonly AdventureController controller;
        private readonly Label status = new Label(), notice = new Label();
        private readonly FlowLayoutPanel actions;
        private readonly ToolTip hints = new ToolTip();
        private readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
        private string actionSignature;
        private Image desktopArtwork;

        private sealed class DongtianAction
        {
            internal string Label, Tip;
            internal int Width;
            internal Func<bool> Run;
        }

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
            content.Controls.Add(title);
            if (controller.IsolatedDesktop && !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ArtDemoPack.EnvironmentVariable)))
            {
                title.ForeColor = Color.FromArgb(40, 83, 71);
                string art = Path.Combine(Environment.GetEnvironmentVariable(ArtDemoPack.EnvironmentVariable) ?? "", "landmark-dongtian.png");
                using (Image source = Image.FromFile(art)) desktopArtwork = new Bitmap(source);
                Panel illustration = new Panel { Width = 640, Height = 148, BackColor = Color.FromArgb(234, 239, 225), Margin = new Padding(0, 0, 0, 8) };
                illustration.Controls.Add(new PictureBox { Image = desktopArtwork, SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(8, 0, 152, 148), AccessibleName = "清虚洞天内苑" });
                illustration.Controls.Add(new Label { Text = "清虚洞天\n一机一洞天\n\n筑基之后经营内苑，播种、炼丹与养器。\n操作使用本隔离档的真实库存与计时。", AutoSize = false, Bounds = new Rectangle(180, 10, 445, 130), ForeColor = Color.FromArgb(40, 83, 71) });
                content.Controls.Add(illustration);
            }
            content.Controls.Add(status); content.Controls.Add(notice); content.Controls.Add(actions);
            Controls.Add(content);
            refresh.Interval = 2000; refresh.Tick += delegate { if (Visible && !IsDisposed) RefreshData(); }; refresh.Start();
            Shown += delegate { RefreshData(); };
        }

        private Button CreateAction(DongtianAction action)
        {
            Button button = new Button { Text = action.Label, Width = action.Width, Height = 34, Margin = new Padding(0, 3, 8, 3) };
            if (!String.IsNullOrEmpty(action.Tip)) hints.SetToolTip(button, action.Tip);
            button.Click += delegate { if (action.Run != null && action.Run()) { } notice.Text = controller.Dongtian == null ? "" : controller.Dongtian.LastNotice; RefreshData(); };
            return button;
        }

        internal void RefreshData()
        {
            if (IsDisposed) return;
            DongtianProgress d = controller.Dongtian;
            if (d == null) { status.Text = "洞天数据不可用。"; notice.Text = ""; SyncActions(new List<DongtianAction>()); return; }
            DateTime utc = DateTime.UtcNow;
            if (!controller.DongtianUnlocked)
            {
                status.Text = "洞天内苑（预览）\r\n\r\n筑基之后，方可开辟洞天、开垦灵田、筑炉炼丹、冶器养灵。\r\n"
                    + "蜃楼带回的水月灵髓届时可入库入药——每一次秘境之行都在为洞天积攒家底。\r\n\r\n当前境界："
                    + controller.Cultivation.RealmName + " · 第" + controller.Cultivation.Layer + "层";
                notice.Text = "";
                SyncActions(new List<DongtianAction>());
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
            foreach (KeyValuePair<string, int> pair in d.Bag) bag.Append(DongtianRecipes.MaterialName(pair.Key) + "×" + pair.Value + "　");
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
            notice.Text = d.LastNotice + (controller.DongtianPersistPending ? "　［档案待落盘 · 自动重试中］" : "");

            // 可用动作集合：与文本同一份 utc 快照推导；集合（标签+提示）不变时
            // 不触碰控件，保证长开窗口下按钮实例稳定（见 SyncActions）。
            List<DongtianAction> available = new List<DongtianAction>();
            foreach (DongtianSeedDef seed in DongtianRecipes.Seeds)
                for (int i = 0; i < d.UnlockedFields; i++)
                    if (d.Fields[i] == null) { int captured = i; available.Add(new DongtianAction { Label = "种" + seed.Name + "→田" + (i + 1), Width = 150, Tip = seed.GrowSeconds / 3600 + " 小时成熟 · 收获 ×" + seed.Yield, Run = delegate { return controller.DongtianPlant(captured, seed.Id); } }); break; }
            for (int i = 0; i < d.UnlockedFields; i++)
                if (d.FieldReady(i, utc)) { int captured = i; available.Add(new DongtianAction { Label = "收获田" + (i + 1), Width = 110, Tip = "成熟作物收进洞天储物", Run = delegate { return controller.DongtianHarvest(captured); } }); }
            int lingsui = controller.LingyuMaterialCount("shuiyue-lingsui");
            if (lingsui > 0) available.Add(new DongtianAction { Label = "灵髓入库（灵域剩 " + lingsui + "）", Width = 190, Tip = "把水月灵髓从灵域收藏带回洞天储物；双向台账幂等入账", Run = delegate { return controller.DongtianImport("shuiyue-lingsui"); } });
            if (d.Furnace == null)
                foreach (DongtianPillDef pill in DongtianRecipes.Pills)
                {
                    DongtianPillDef captured = pill;
                    available.Add(new DongtianAction { Label = "炼" + pill.Name, Width = 120, Tip = pill.EffectText + " · 材料：" + MaterialsText(pill.Materials), Run = delegate { return controller.DongtianStartPill(captured.Id); } });
                }
            else if (d.FurnaceReady(utc)) available.Add(new DongtianAction { Label = "收丹", Width = 90, Tip = "丹成即服，限时增益立即生效", Run = delegate { return controller.DongtianCollectPill(); } });
            if (d.Forge == null)
            {
                foreach (DongtianGearDef gear in DongtianRecipes.Gears)
                    if (!d.HasGear(gear.Id))
                    {
                        DongtianGearDef captured = gear;
                        available.Add(new DongtianAction { Label = "铸" + gear.Name, Width = 110, Tip = gear.EffectText + " · 材料：" + MaterialsText(gear.Materials), Run = delegate { return controller.DongtianStartGear(captured.Id); } });
                    }
            }
            else if (d.ForgeReady(utc)) available.Add(new DongtianAction { Label = "收器", Width = 90, Tip = "器灵铸成，常驻生效", Run = delegate { return controller.DongtianCollectGear(); } });
            SyncActions(available);
        }

        // 动作签名判变：只有集合真正变化时才清空重建；否则保留全部按钮实例。
        private void SyncActions(List<DongtianAction> available)
        {
            string signature = String.Join("|", available.Select(a => a.Label + "?" + a.Width + "?" + a.Tip).ToArray());
            if (signature == actionSignature) return;
            actionSignature = signature;
            actions.SuspendLayout();
            try
            {
                foreach (Control old in actions.Controls) old.Dispose();
                actions.Controls.Clear();
                foreach (DongtianAction action in available) actions.Controls.Add(CreateAction(action));
            }
            finally { actions.ResumeLayout(true); }
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
            Dictionary<string, int> need = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string m in materials) need[m] = (need.ContainsKey(m) ? need[m] : 0) + 1;
            StringBuilder text = new StringBuilder();
            foreach (KeyValuePair<string, int> pair in need) text.Append(DongtianRecipes.MaterialName(pair.Key) + "×" + pair.Value + " ");
            return text.ToString().Trim();
        }

        // 测试钩子：读取当前动作按钮（实例稳定性断言用），不暴露可写集合。
        internal Control[] ActionControlsSnapshot() { return actions.Controls.Cast<Control>().ToArray(); }
        internal string ActionSignatureForTest { get { return actionSignature; } }

        protected override void OnFormClosing(FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } base.OnFormClosing(e); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { refresh.Stop(); refresh.Dispose(); hints.Dispose(); if (desktopArtwork != null) { desktopArtwork.Dispose(); desktopArtwork = null; } }
            base.Dispose(disposing);
        }
    }
}
