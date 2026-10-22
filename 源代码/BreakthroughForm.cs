using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace NetBunny
{
    // 破境仪式：功能性突破交互——确认条件 → 查看本境契机 → 选择辅助材料 →
    // 看清成功率与保底 → 发起突破 → 展示结算。不堆视觉特效，信息与决策优先。
    // 成功率、材料、保底与失败感悟全部读自修仙核心配置，本窗体不私藏数值。
    internal sealed class BreakthroughForm : Form
    {
        // 契机展示文案（id 与修仙核心 GrantOpportunity 的约定一致）。
        private static readonly string[] OpportunityIds = { "treasure", "realm", "baiye", "insight" };
        private static readonly string[] OpportunityNames = { "天材地宝", "秘境寻宝", "百业悟道", "长期苦修" };
        private static readonly string[] OpportunityHints = {
            "蜃楼主镜凝出的「水月灵髓」在手（千面蜃楼 · 炼气十层以上首胜蜃楼主）",
            "在本境通关千面蜃楼；破境后需再次通关，旧成绩不会重复授予契机",
            "在本境升级道途或解锁新机缘时，若任一道途达 Lv.3 或已有「天机阵图」，即可悟得",
            "本境圆满后持续吐纳，破境感悟满 " + CultivationBalance.InsightOpportunityThreshold + " 自悟"
        };

        private readonly AdventureController controller;
        private readonly Label stateCard = new Label(), chanceCard = new Label(), materialLine = new Label();
        private readonly Label opportunityTitle = new Label();
        private readonly Label[] opportunityLines = new Label[OpportunityIds.Length];
        private readonly CheckBox useMaterial = new CheckBox();
        private readonly Button attempt = new Button(), closePanel = new Button();
        private readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
        private bool updating;

        internal BreakthroughForm(AdventureController controller)
        {
            this.controller = controller; Text = "桌面修仙传 · 破境仪式";
            Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            Width = Math.Min(700, workArea.Width - 24); Height = Math.Min(860, workArea.Height - 40);
            MinimumSize = new Size(Math.Min(380, workArea.Width - 24), Math.Min(420, workArea.Height - 40));
            StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(245, 244, 251);
            Font = new Font("Microsoft YaHei UI", 10);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            FlowLayoutPanel content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(22), BackColor = BackColor };
            Controls.Add(content);
            Label title = Line("破境仪式 · 境界突破", 42); title.Font = new Font(Font.FontFamily, 18, FontStyle.Bold); title.ForeColor = Color.FromArgb(73, 49, 132); content.Controls.Add(title);
            content.Controls.Add(Line("本境圆满后，需握得至少一条破境契机方可尝试突破。可用路径按当前境界显示，任一即可；失败不损失境界、灵根与修为根基，感悟深厚后保底必成。", 52));

            content.Controls.Add(Line("当前修行", 24));
            stateCard.Width = 640; stateCard.Height = 84; stateCard.AutoSize = true; stateCard.MaximumSize = new Size(640, 0); stateCard.MinimumSize = new Size(0, 84);
            stateCard.BackColor = Color.White; stateCard.Padding = new Padding(12, 9, 12, 9); stateCard.BorderStyle = BorderStyle.FixedSingle; stateCard.Margin = new Padding(0, 2, 0, 7); content.Controls.Add(stateCard);

            opportunityTitle.AutoSize = true; opportunityTitle.MaximumSize = new Size(640, 0);
            opportunityTitle.MinimumSize = new Size(0, 24); opportunityTitle.Margin = new Padding(0, 4, 0, 4);
            content.Controls.Add(opportunityTitle);
            for (int i = 0; i < OpportunityIds.Length; i++)
            {
                Label line = Line("", 24); line.ForeColor = Color.FromArgb(91, 87, 107);
                opportunityLines[i] = line; content.Controls.Add(line);
            }

            content.Controls.Add(Line("辅助材料", 24));
            useMaterial.Width = 640; useMaterial.Height = 28; useMaterial.BackColor = Color.Transparent; useMaterial.Margin = new Padding(0, 2, 0, 2);
            useMaterial.CheckedChanged += delegate { if (!updating) RefreshData(); };
            content.Controls.Add(useMaterial);
            materialLine.Width = 640; materialLine.Height = 20; materialLine.ForeColor = Color.FromArgb(150, 146, 168); materialLine.Margin = new Padding(18, 0, 0, 6); content.Controls.Add(materialLine);

            content.Controls.Add(Line("突破测算", 24));
            chanceCard.Width = 640; chanceCard.Height = 66; chanceCard.AutoSize = true; chanceCard.MaximumSize = new Size(640, 0); chanceCard.MinimumSize = new Size(0, 66);
            chanceCard.BackColor = Color.White; chanceCard.Padding = new Padding(12, 9, 12, 9); chanceCard.BorderStyle = BorderStyle.FixedSingle; chanceCard.Margin = new Padding(0, 2, 0, 7); content.Controls.Add(chanceCard);

            FlowLayoutPanel actions = Row();
            Configure(attempt, "发起破境", 170, Attempt);
            Configure(closePanel, "收起仪式", 130, delegate { Hide(); });
            actions.Controls.Add(attempt); actions.Controls.Add(closePanel); content.Controls.Add(actions);
            content.Controls.Add(Line("破境只改写修仙档案：境界提升后解锁新的层数配置与能力，契机随之清空，需要在新境界重新寻得。", 34));

            refresh.Interval = 500; refresh.Tick += delegate { if (Visible) RefreshData(); }; refresh.Start();
            Shown += delegate { RefreshData(); };
            if (controller.IsolatedDesktop) DesktopFeatureStyle.Apply(this);
            DesktopFeatureStyle.BindResponsiveColumn(content);
        }

        private void Attempt()
        {
            CultivationProgress practice = controller.Cultivation;
            if (practice == null || controller.IsDemo || !practice.CanAttemptBreakthrough) return;
            bool withMaterial = useMaterial.Checked && useMaterial.Enabled;
            int percent = practice.BreakthroughPercent(withMaterial, withMaterial);
            string question = "当前境界：" + practice.RealmName + " · 第" + practice.Layer + "层（圆满）" +
                "\n目标境界：" + CultivationRealms.For(practice.RealmIndex + 1).Name +
                "\n本次破境成功率：" + percent + "%" + (percent >= 100 ? "（保底必成）" : "") +
                (withMaterial ? "\n辅助材料：水月灵髓 ×1（成败皆消耗）" : "") +
                "\n失败不损失已积累的修为与根基。\n\n现在发起破境吗？";
            if (MessageBox.Show(this, question, "破境仪式", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            bool success = controller.TryBreakthrough(withMaterial);
            MessageBox.Show(this, controller.Cultivation.LastNotice, success ? "破境成功" : "破境未成", MessageBoxButtons.OK,
                success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            RefreshData();
        }

        private Label Line(string text, int height) { return new Label { Text = text, Width = 640, Height = height, AutoSize = true, MaximumSize = new Size(640, 0), MinimumSize = new Size(0, height), Margin = new Padding(0, 4, 0, 4) }; }
        private FlowLayoutPanel Row() { return new FlowLayoutPanel { Width = 650, Height = 40, AutoSize = true, MinimumSize = new Size(0, 40), WrapContents = true, Margin = new Padding(0, 3, 0, 3) }; }
        private void Configure(Button button, string text, int width, Action action) { button.Text = text; button.Width = width; button.Height = 34; button.FlatStyle = FlatStyle.Flat; button.BackColor = Color.FromArgb(115, 78, 191); button.ForeColor = Color.White; button.FlatAppearance.BorderSize = 0; button.Cursor = Cursors.Hand; button.Click += delegate { if (!updating) action(); }; }

        internal void RefreshData()
        {
            if (IsDisposed) return; updating = true;
            try
            {
                CultivationProgress practice = controller.Cultivation;
                CultivationRealmDef def = practice.RealmDef;
                bool materialAvailable = controller.BreakthroughMaterialCount >= Math.Max(1, def.MaterialCount) && def.MaterialId.Length > 0;

                stateCard.Text = practice.Mortal
                    ? "凡人 · 尚未觉醒灵根\r\n有效击杀 " + practice.MortalKills + "/" + CultivationBalance.MortalAwakenKills + "\r\n先完成十次红尘历练，再从手札主动觉醒。"
                    : (String.IsNullOrEmpty(practice.DaoTitle) ? "无名修士" : practice.DaoTitle) + " · " + practice.RealmName + " · 第" + practice.Layer + "层" +
                    (practice.RealmComplete ? "（圆满）" : "") +
                    "\r\n修为 " + practice.Qi + " / " + (practice.RealmComplete ? "圆满" : practice.NextLayerCost.ToString(CultureInfo.InvariantCulture)) +
                    " · 破境感悟 " + practice.BreakthroughInsight + " · 已尝试破境 " + practice.BreakthroughAttempts + " 次" +
                    "\r\n" + (practice.IsFinalRealm ? "已至目前开放的最高境界。" : "下一境界：" + CultivationRealms.For(practice.RealmIndex + 1).Name +
                    "（" + CultivationRealms.For(practice.RealmIndex + 1).LayersMax + " 层，独立层数配置）");

                bool canSeek = !practice.Mortal && !practice.IsFinalRealm;
                opportunityTitle.Text = practice.Mortal ? "觉醒灵根后开启破境修行" : practice.IsFinalRealm ? "已至目前开放的最高境界"
                    : "破境契机 · 本境 " + def.OpportunityPool.Length + " 途，任一即可";
                for (int i = 0; i < OpportunityIds.Length; i++)
                {
                    bool supported = canSeek && Array.IndexOf(def.OpportunityPool, OpportunityIds[i]) >= 0;
                    opportunityLines[i].Visible = supported;
                    if (!supported) { opportunityLines[i].Text = ""; continue; }
                    bool owned = practice.HasOpportunity(OpportunityIds[i]);
                    opportunityLines[i].Text = (owned ? "✓ " : "○ ") + OpportunityNames[i] + " —— " + OpportunityHints[i] + (owned ? "（已悟得）" : "");
                    opportunityLines[i].ForeColor = owned ? Color.FromArgb(34, 126, 84) : (controller.IsolatedDesktop ? DesktopFeatureStyle.Ink : Color.FromArgb(91, 87, 107));
                }

                bool canUseMaterial = materialAvailable && canSeek;
                useMaterial.Enabled = canUseMaterial;
                if (!canUseMaterial && useMaterial.Checked) useMaterial.Checked = false;
                useMaterial.Text = def.MaterialId.Length == 0
                    ? "本境界未配置辅助材料"
                    : "使用 水月灵髓 ×" + Math.Max(1, def.MaterialCount) + "（成功率 +" + def.MaterialBonusPercent + "%，成败皆消耗）";
                materialLine.Text = def.MaterialId.Length == 0
                    ? ""
                    : materialAvailable
                        ? "库存：水月灵髓 ×" + controller.BreakthroughMaterialCount + "（灵域历练 · 收藏）"
                        : "尚无库存：炼气十层以上首胜蜃楼主可凝出一滴（千面蜃楼 · 主镜）";

                bool withMaterial = useMaterial.Checked && useMaterial.Enabled;
                int percent = practice.BreakthroughPercent(withMaterial, withMaterial);
                string chanceText;
                if (practice.Mortal) chanceText = "凡人尚未踏入仙途：先在修行面板完成灵根觉醒。";
                else if (practice.IsFinalRealm) chanceText = "已至巅峰，无需破境。";
                else if (!practice.RealmComplete) chanceText = "尚未圆满：修至" + practice.RealmName + "第" + practice.LayersMax + "层后方可尝试破境（当前第 " + practice.Layer + " 层）。";
                else if (practice.BreakthroughOpportunities.Count == 0 || practice.CurrentOpportunityCount == 0) chanceText = "本境已圆满，尚缺破境契机：上方本境可用路径任得其一即可开启仪式（契机须在" + practice.RealmName + "重新悟得）。";
                else
                {
                    chanceText = "本次破境成功率：" + percent + "%（基础 " + def.BreakthroughBasePercent + "% + 感悟加成 " +
                        (percent - def.BreakthroughBasePercent - (withMaterial ? def.MaterialBonusPercent : 0)) + "%" +
                        (withMaterial ? " + 材料 " + def.MaterialBonusPercent + "%" : "") + "）" +
                        (percent >= 100 ? "\r\n破境感悟已足，保底必成。" :
                        "\r\n失败后破境感悟 +" + def.FailureInsight + "；感悟满 " + def.GuaranteedInsight + " 时保底必成。");
                }
                chanceCard.Text = chanceText;

                attempt.Enabled = practice.CanAttemptBreakthrough && !controller.IsDemo;
                attempt.Text = practice.CanAttemptBreakthrough ? "发起破境（" + percent + "%）" : "发起破境";
            }
            finally { updating = false; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } base.OnFormClosing(e); }
        protected override void Dispose(bool disposing) { if (disposing) { refresh.Stop(); refresh.Dispose(); } base.Dispose(disposing); }
    }
}
