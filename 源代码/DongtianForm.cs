using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
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
        private readonly Label overview = new Label(), status = new Label(), notice = new Label(), plantingGuide = new Label(), effects = new Label { Name = "cave-effects" };
        private readonly FlowLayoutPanel content, quickActions, actions;
        private readonly ToolTip hints = new ToolTip();
        private readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
        private string actionSignature;
        private readonly Dictionary<string, ActionView> actionViews = new Dictionary<string, ActionView>(StringComparer.Ordinal);
        private readonly SlotView[] slotViews = new SlotView[DongtianProgress.MaxFields + 2];
        private Control[] actionSnapshot = new Control[0];

        private sealed class SlotView
        {
            internal FlowLayoutPanel Root, Actions;
            internal Label State;
        }

        private sealed class DongtianAction
        {
            internal string Label, Tip;
            internal int Width;
            internal Func<bool> Run;
            internal bool Primary;
            internal int Slot = -1;
            internal bool Visible = true;
            internal string[] Materials;
        }
        private sealed class ActionView
        {
            internal DongtianAction Action;
            internal Control Root;
            internal Button Button;
            internal Label Detail;
        }

        internal DongtianForm(AdventureController controller)
        {
            this.controller = controller;
            Text = "洞天经营 · 炼丹炼器";
            Width = 720; Height = 560; StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(380, 420);
            BackColor = Color.FromArgb(250, 247, 240); Font = new Font("Microsoft YaHei UI", 10F);
            content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(20), BackColor = BackColor };
            Label title = new Label { Text = "洞天经营", AutoSize = true, Font = new Font(Font.FontFamily, 17, FontStyle.Bold), ForeColor = Color.FromArgb(73, 49, 132), Margin = new Padding(0, 0, 0, 8) };
            overview.Width = 640; overview.Margin = new Padding(0, 0, 0, 5);
            plantingGuide.Width = 640; plantingGuide.ForeColor = Color.FromArgb(130, 84, 32); plantingGuide.Margin = new Padding(0, 4, 0, 6);
            status.AutoSize = false; status.Width = 640; status.Margin = new Padding(0, 10, 0, 8);
            notice.AutoSize = false; notice.Width = 640; notice.Height = 34; notice.ForeColor = Color.FromArgb(130, 84, 32);
            effects.Width = 640; effects.Margin = new Padding(0, 8, 0, 10);
            quickActions = new FlowLayoutPanel { Width = 650, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            actions = new FlowLayoutPanel { Width = 650, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            for (int i = 0; i < slotViews.Length; i++)
            {
                SlotView slot = new SlotView();
                slot.Root = new FlowLayoutPanel { Name = i < DongtianProgress.MaxFields ? "cave-field-" + i : i == DongtianProgress.MaxFields ? "cave-furnace" : "cave-forge", Width = 650, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 0, 7) };
                slot.State = new Label { Width = 640, AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
                slot.Actions = new FlowLayoutPanel { Width = 650, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
                slot.Root.Controls.Add(slot.State); slot.Root.Controls.Add(slot.Actions);
                slotViews[i] = slot; quickActions.Controls.Add(slot.Root);
            }
            content.Controls.Add(title);
            // Keep the first viewport for production state and frequent actions.
            // The existing landmark art remains in the world; no image is made
            // or loaded for this compact operating header.
            if (controller.IsolatedDesktop) title.Text = "洞天经营 · 清虚内苑";
            content.Controls.Add(overview); content.Controls.Add(quickActions);
            content.Controls.Add(notice); content.Controls.Add(plantingGuide); content.Controls.Add(status); content.Controls.Add(effects); content.Controls.Add(actions);
            Controls.Add(content);
            refresh.Interval = 2000; refresh.Tick += delegate { if (Visible && !IsDisposed) RefreshData(); }; refresh.Start();
            Shown += delegate { RefreshData(); };
            if (controller.IsolatedDesktop) DesktopFeatureStyle.Apply(this);
            DesktopFeatureStyle.BindResponsiveColumn(content);
        }

        private ActionView CreateAction(DongtianAction action)
        {
            ActionView view = new ActionView { Action = action };
            Button button = new Button { Text = action.Label, Width = action.Width, Height = 34, Margin = new Padding(0, 3, 8, 3) };
            view.Button = button;
            if (controller.IsolatedDesktop) DesktopFeatureStyle.ApplyButton(button);
            button.Click += delegate
            {
                // A queued event can arrive after inventory or persistence state
                // changes. The core still validates the real recipe on execution.
                if (!button.Enabled || controller.DongtianPersistPending) { RefreshData(); return; }
                if (view.Action.Run != null) view.Action.Run();
                RefreshData();
            };
            if (action.Primary) view.Root = button;
            else
            {
                FlowLayoutPanel row = new FlowLayoutPanel { Width = 650, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 10) };
                view.Detail = new Label { Width = 440, Margin = new Padding(0, 5, 0, 0), ForeColor = controller.IsolatedDesktop ? DesktopFeatureStyle.Ink : ForeColor };
                row.Controls.Add(button); row.Controls.Add(view.Detail); view.Root = row;
                if (controller.IsolatedDesktop) { row.BackColor = DesktopFeatureStyle.Paper; view.Detail.BackColor = DesktopFeatureStyle.Paper; }
            }
            return view;
        }

        internal void RefreshData()
        {
            if (IsDisposed) return;
            DongtianProgress d = controller.Dongtian;
            if (d == null) { overview.Text = "洞天数据不可用。"; status.Text = plantingGuide.Text = notice.Text = effects.Text = ""; quickActions.Visible = false; SyncActions(new List<DongtianAction>()); return; }
            DateTime utc = DateTime.UtcNow;
            if (!controller.DongtianUnlocked)
            {
                status.Text = "洞天内苑（预览）\r\n\r\n筑基之后，方可开辟洞天、开垦灵田、筑炉炼丹、冶器养灵。\r\n"
                    + "蜃楼反复带回的镜花水月届时可入库炼丹；稀有水月灵髓也保留原有丹方。\r\n\r\n当前境界："
                    + controller.Cultivation.RealmName + " · 第" + controller.Cultivation.Layer + "层";
                notice.Text = "";
                overview.Text = "洞天内苑 · 筑基后开放"; plantingGuide.Text = "";
                effects.Text = ""; quickActions.Visible = false;
                SyncActions(new List<DongtianAction>());
                return;
            }
            StringBuilder text = new StringBuilder();
            overview.Text = "洞天等级 Lv." + d.Level + " · 灵田 " + d.UnlockedFields + " 畦 · 丹炉 1 座 · 器位 1 处";
            quickActions.Visible = true;
            DongtianExpansionDef expansion = DongtianRecipes.Expansion(d.Level);
            for (int i = 0; i < DongtianProgress.MaxFields; i++)
            {
                slotViews[i].Root.Visible = i < d.UnlockedFields;
                if (i >= d.UnlockedFields) continue;
                string slot = d.Fields[i];
                if (slot == null) { slotViews[i].State.Text = "灵田·" + (i + 1) + "：空置 · 选择作物播种"; continue; }
                string recipe; long ready;
                if (DongtianField(slot, out recipe, out ready))
                {
                    DongtianSeedDef seed = DongtianRecipes.Seed(recipe);
                    slotViews[i].State.Text = "灵田·" + (i + 1) + "：" + (seed == null ? recipe : seed.Name) + (ready <= utc.Ticks ? " · 已成熟，可收获" : " · 生长中 · 剩余" + RemainingTime(ready, utc));
                }
                else slotViews[i].State.Text = "灵田·" + (i + 1) + "：状态待核实";
            }
            slotViews[DongtianProgress.MaxFields].State.Text = "丹炉：" + SlotText(d.Furnace, "丹", utc);
            slotViews[DongtianProgress.MaxFields + 1].State.Text = "器位：" + SlotText(d.Forge, "器", utc);
            plantingGuide.Text = "先种灵谷：2小时收2份，可炼聚气散或扩建。\r\n凝神花：6小时收2份，用于固神丹、悟真丹；剑心草：12小时收1份，用于养器。播种无需材料。\r\n蜃楼的镜花水月×2可炼镜花凝露丹，伤害+8%持续12小时；与凝露丹共用药力，不叠加。";
            if (expansion == null) text.AppendLine("洞天已满级 · 四畦灵田均已开辟");
            else text.AppendLine("下次扩建：Lv." + expansion.ToLevel + " · 新增一畦灵田 · 材料：" + MaterialsText(expansion.Materials));
            StringBuilder bag = new StringBuilder("洞天储物：");
            foreach (KeyValuePair<string, int> pair in d.Bag) bag.Append(DongtianRecipes.MaterialName(pair.Key) + "×" + pair.Value + "　");
            if (d.Bag.Count == 0) bag.Append("（空）——灵田收获或灵域入库后入库在册");
            text.AppendLine(bag.ToString());
            effects.Text = EffectsText(d, utc);
            status.Text = text.ToString().TrimEnd();
            notice.Text = d.LastNotice + (controller.DongtianPersistPending ? "　［档案待落盘 · 自动重试中］" : "");

            // 可用动作集合：与文本同一份 utc 快照推导；集合（标签+提示）不变时
            // 不触碰控件，保证长开窗口下按钮实例稳定（见 SyncActions）。
            List<DongtianAction> available = new List<DongtianAction>();
            for (int i = 0; i < d.UnlockedFields; i++)
            {
                int captured = i;
                available.Add(new DongtianAction { Label = "收获田" + (i + 1), Slot = i, Width = 110, Primary = true, Visible = d.FieldReady(i, utc), Tip = "成熟作物收进洞天储物", Run = delegate { return controller.DongtianHarvest(captured); } });
                foreach (DongtianSeedDef seed in DongtianRecipes.Seeds)
                {
                    DongtianSeedDef capturedSeed = seed;
                    available.Add(new DongtianAction { Label = "种" + seed.Name + "→田" + (i + 1), Slot = i, Width = 150, Primary = true, Visible = d.Fields[i] == null, Tip = seed.GrowSeconds / 3600 + " 小时成熟 · 收获 ×" + seed.Yield, Run = delegate { return controller.DongtianPlant(captured, capturedSeed.Id); } });
                }
            }
            int lingsui = controller.LingyuMaterialCount("shuiyue-lingsui");
            if (lingsui > 0) available.Add(new DongtianAction { Label = "灵髓入库", Width = 160, Tip = "灵域收藏剩 " + lingsui + " · 入库后可炼凝露丹；入库会转出灵域收藏中的灵髓。", Run = delegate { return controller.DongtianImport("shuiyue-lingsui"); } });
            int mirrorMaterial = controller.LingyuMaterialCount("jinghua-shuiyue");
            if (mirrorMaterial > 0) available.Add(new DongtianAction { Label = "镜花水月入库", Width = 160, Tip = "灵域收藏剩 " + mirrorMaterial + " · 每次转入1份，2份可炼镜花凝露丹；蜃楼宝箱、镜妖与主镜可重复获得。", Run = delegate { return controller.DongtianImport("jinghua-shuiyue"); } });
            if (expansion != null)
            {
                int expectedLevel = d.Level;
                available.Add(new DongtianAction { Label = "扩建洞天→Lv." + expansion.ToLevel, Width = 185, Materials = expansion.Materials,
                    Tip = "新增一畦灵田 · 消耗：" + MaterialsText(expansion.Materials), Run = delegate { return controller.DongtianExpand(expectedLevel); } });
            }
            if (d.Furnace == null)
                foreach (DongtianPillDef pill in DongtianRecipes.Pills)
                {
                    DongtianPillDef captured = pill;
                    available.Add(new DongtianAction { Label = "炼" + pill.Name, Width = 160, Materials = pill.Materials, Tip = pill.EffectText + "\r\n炼制 " + (pill.BrewSeconds * d.FurnaceTimeFactor / 60).ToString("0") + " 分钟 · 材料：" + MaterialsText(pill.Materials), Run = delegate { return controller.DongtianStartPill(captured.Id); } });
                }
            available.Add(new DongtianAction { Label = "收丹", Slot = DongtianProgress.MaxFields, Width = 90, Primary = true, Visible = d.FurnaceReady(utc), Tip = "丹成即服，限时增益立即生效", Run = delegate { return controller.DongtianCollectPill(); } });
            if (d.Forge == null)
            {
                foreach (DongtianGearDef gear in DongtianRecipes.Gears)
                    if (!d.HasGear(gear.Id))
                    {
                        DongtianGearDef captured = gear;
                        available.Add(new DongtianAction { Label = "铸" + gear.Name, Width = 160, Materials = gear.Materials, Tip = gear.EffectText + "\r\n炼制 " + gear.ForgeSeconds / 60 + " 分钟 · 材料：" + MaterialsText(gear.Materials), Run = delegate { return controller.DongtianStartGear(captured.Id); } });
                    }
            }
            available.Add(new DongtianAction { Label = "收器", Slot = DongtianProgress.MaxFields + 1, Width = 90, Primary = true, Visible = d.ForgeReady(utc), Tip = "器灵铸成，常驻生效", Run = delegate { return controller.DongtianCollectGear(); } });
            SyncActions(available);
        }

        // Inventory, wording and pending persistence update existing controls.
        // Only a genuinely added/retired operation creates/disposes its own row.
        private void SyncActions(List<DongtianAction> available)
        {
            actionSignature = String.Join("|", available.Where(a => a.Visible).Select(a => a.Label).ToArray());
            HashSet<string> present = new HashSet<string>(available.Select(a => a.Label), StringComparer.Ordinal);
            content.SuspendLayout(); quickActions.SuspendLayout(); actions.SuspendLayout();
            try
            {
                foreach (string key in actionViews.Keys.Where(k => !present.Contains(k)).ToArray()) { actionViews[key].Root.Dispose(); actionViews.Remove(key); }
                int recipeIndex = 0;
                foreach (DongtianAction action in available)
                {
                    ActionView view;
                    if (!actionViews.TryGetValue(action.Label, out view))
                    {
                        view = CreateAction(action); actionViews.Add(action.Label, view);
                        (action.Slot >= 0 ? slotViews[action.Slot].Actions : actions).Controls.Add(view.Root);
                    }
                    view.Action = action;
                    view.Root.Visible = action.Visible;
                    string missing = MissingMaterials(action.Materials);
                    string condition = controller.DongtianPersistPending ? "档案待落盘：自动重试完成后可操作。" : controller.IsDemo ? "演示模式不可操作。" : missing;
                    view.Button.Enabled = condition.Length == 0;
                    view.Button.AccessibleDescription = action.Tip + (condition.Length == 0 ? "" : " · " + condition);
                    hints.SetToolTip(view.Button, view.Button.AccessibleDescription);
                    if (view.Detail != null) view.Detail.Text = action.Tip + (condition.Length == 0 ? "\r\n材料齐备，可开始。" : "\r\n" + condition);
                    if (action.Slot < 0) { if (actions.Controls.GetChildIndex(view.Root) != recipeIndex) actions.Controls.SetChildIndex(view.Root, recipeIndex); recipeIndex++; }
                }
                actionSnapshot = available.Where(a => a.Visible).Select(a => (Control)actionViews[a.Label].Button).ToArray();
            }
            finally { quickActions.ResumeLayout(true); actions.ResumeLayout(true); content.ResumeLayout(true); content.PerformLayout(); }
        }

        private string MissingMaterials(string[] materials)
        {
            if (materials == null) return "";
            Dictionary<string, int> need = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string material in materials) need[material] = (need.ContainsKey(material) ? need[material] : 0) + 1;
            List<string> missing = new List<string>();
            foreach (KeyValuePair<string, int> pair in need)
            {
                int stock = controller.Dongtian.CountOf(pair.Key);
                if (stock < pair.Value) missing.Add(DongtianRecipes.MaterialName(pair.Key) + "缺" + (pair.Value - stock) + "（现有" + stock + "/需" + pair.Value + "）");
            }
            return missing.Count == 0 ? "" : "材料不足：" + String.Join("、", missing.ToArray());
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
            DongtianPillDef pill = DongtianRecipes.Pill(recipe); DongtianGearDef gear = DongtianRecipes.Gear(recipe);
            string name = kind == "丹" && pill != null ? pill.Name : kind == "器" && gear != null ? gear.Name : recipe;
            return name + (ready <= utc.Ticks ? " · 已成，可收" : " · 炼制中 · 剩余" + RemainingTime(ready, utc));
        }
        private static string RemainingTime(long ready, DateTime utc)
        {
            TimeSpan remaining = TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling((ready - utc.Ticks) / (double)TimeSpan.TicksPerSecond)));
            return remaining.TotalHours >= 1 ? (int)remaining.TotalHours + "小时" + remaining.Minutes + "分" : remaining.Minutes + "分" + remaining.Seconds + "秒";
        }
        private static string EffectsText(DongtianProgress d, DateTime utc)
        {
            StringBuilder text = new StringBuilder("洞天贡献（仅洞天增益，不含境界、功法等其他来源）\r\n");
            text.AppendLine("吐纳×" + d.QiRateFactor(utc).ToString("0.##", CultureInfo.InvariantCulture) + " · 伤害×" + d.DamageFactor(utc).ToString("0.##", CultureInfo.InvariantCulture)
                + " · 调息时长×" + d.ReviveTimeFactor(utc).ToString("0.##", CultureInfo.InvariantCulture) + " · 炼丹时长×" + d.FurnaceTimeFactor.ToString("0.##", CultureInfo.InvariantCulture));
            foreach (DongtianPillDef pill in DongtianRecipes.Pills.GroupBy(p => p.BuffId).Select(group => group.First()))
            {
                long expires;
                if (d.Buffs.TryGetValue(pill.BuffId, out expires) && expires > utc.Ticks) text.AppendLine(pill.Name + " · " + pill.EffectText + " · 剩余" + RemainingTime(expires, utc));
            }
            foreach (string id in d.Gears.OrderBy(id => id, StringComparer.Ordinal))
            { DongtianGearDef gear = DongtianRecipes.Gear(id); if (gear != null) text.AppendLine(gear.Name + " · " + gear.EffectText); }
            return text.ToString().TrimEnd();
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
        internal Control[] ActionControlsSnapshot() { return (Control[])actionSnapshot.Clone(); }
        internal string ActionSignatureForTest { get { return actionSignature; } }

        protected override void OnFormClosing(FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } base.OnFormClosing(e); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { refresh.Stop(); refresh.Dispose(); hints.Dispose(); actionViews.Clear(); }
            base.Dispose(disposing);
        }
    }
}
