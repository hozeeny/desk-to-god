using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace NetBunny
{
    // 灵域历练：四座软件灵域的入口、千面蜃楼秘境探索（入口/节点/路径选择/
    // 随机事件/机缘/虚拟战斗/暂离重访），以及材料、功法残卷与传承收藏。
    // 灵域识别规则全部复用百业道碑的类别定义；进入秘境只生成虚拟妖物，
    // 永不触发真实文件删除、进程关闭或内存回收。
    internal sealed class LingyuForm : Form
    {
        private readonly AdventureController controller;
        private readonly Label[] realmNames = new Label[LingyuCatalog.Realms.Length];
        private readonly Label[] realmStates = new Label[LingyuCatalog.Realms.Length];
        private readonly Button[] realmEnters = new Button[LingyuCatalog.Realms.Length];
        private readonly Label nodeTitle = new Label(), nodeText = new Label(), expeditionHint = new Label();
        private readonly Button[] choices = new Button[3];
        private readonly ListBox log = new ListBox(), collection = new ListBox();
        private readonly Button closePanel = new Button();
        private readonly Button continueExpedition = new Button(), suspendExpedition = new Button();
        private readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
        private bool updating;

        internal LingyuForm(AdventureController controller)
        {
            this.controller = controller; Text = "桌面修仙传 · 灵域历练";
            Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            Width = Math.Min(720, workArea.Width - 24); Height = Math.Min(880, workArea.Height - 40);
            MinimumSize = new Size(Math.Min(380, workArea.Width - 24), Math.Min(420, workArea.Height - 40));
            StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(245, 244, 251);
            Font = new Font("Microsoft YaHei UI", 10);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            FlowLayoutPanel content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(22), BackColor = BackColor };
            Controls.Add(content);
            Label title = Line("灵域历练 · 软件灵域与机缘", 42); title.Font = new Font(Font.FontFamily, 18, FontStyle.Bold); title.ForeColor = Color.FromArgb(73, 49, 132); content.Controls.Add(title);
            FlowLayoutPanel directory = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Width = 650, Margin = Padding.Empty };
            directory.Controls.Add(Line("四座灵域", 24));
            for (int i = 0; i < LingyuCatalog.Realms.Length; i++)
            {
                LingyuRealmDef realm = LingyuCatalog.Realms[i];
                FlowLayoutPanel row = Row();
                row.FlowDirection = FlowDirection.TopDown; row.WrapContents = false;
                Label name = new Label { Text = realm.Name, Width = 118, Height = 34, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold), ForeColor = Color.FromArgb(73, 49, 132), Margin = new Padding(0, 3, 0, 0) };
                Label state = new Label { Text = "", Width = 390, Height = 34, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(91, 87, 107), Margin = new Padding(0, 3, 0, 0) };
                Button enter = new Button(); int captured = i;
                Configure(enter, "进入", 96, delegate { controller.LingyuEnter(LingyuCatalog.Realms[captured].Id); RefreshData(); });
                row.Controls.Add(name); row.Controls.Add(state); row.Controls.Add(enter);
                realmNames[i] = name; realmStates[i] = state; realmEnters[i] = enter;
                directory.Controls.Add(row);
                Label desc = Line(realm.Description, 20); desc.ForeColor = Color.FromArgb(150, 146, 168); directory.Controls.Add(desc);
            }

            content.Controls.Add(Line("蜃楼探索", 24));
            nodeTitle.Width = 640; nodeTitle.Height = 30; nodeTitle.Font = new Font(Font.FontFamily, 13, FontStyle.Bold); nodeTitle.ForeColor = Color.FromArgb(73, 49, 132); content.Controls.Add(nodeTitle);
            nodeText.Width = 640; nodeText.Height = 56; nodeText.AutoSize = true; nodeText.MaximumSize = new Size(640, 0); nodeText.MinimumSize = new Size(0, 56); nodeText.BackColor = Color.White; nodeText.Padding = new Padding(10, 8, 10, 8); nodeText.BorderStyle = BorderStyle.FixedSingle; nodeText.Margin = new Padding(0, 2, 0, 7); content.Controls.Add(nodeText);
            FlowLayoutPanel choiceRow = Row();
            for (int i = 0; i < choices.Length; i++)
            {
                Button choice = new Button(); int captured = i;
                Configure(choice, "", 196, delegate { controller.LingyuChoose(captured); RefreshData(); });
                choices[i] = choice; choiceRow.Controls.Add(choice);
            }
            content.Controls.Add(choiceRow);
            expeditionHint.Width = 640; expeditionHint.AutoSize = true; expeditionHint.MinimumSize = new Size(0, 26); expeditionHint.MaximumSize = new Size(640, 0); expeditionHint.ForeColor = Color.FromArgb(173, 113, 37); content.Controls.Add(expeditionHint);
            log.Width = 640; log.Height = 118; log.IntegralHeight = false; log.Font = Font; log.BackColor = Color.FromArgb(250, 249, 253); log.AccessibleName = "蜃楼探索记录"; content.Controls.Add(log);

            content.Controls.Add(Line("收藏 · 材料 / 功法残卷 / 传承", 24));
            collection.Width = 640; collection.Height = 128; collection.IntegralHeight = false; collection.Font = Font; collection.BackColor = Color.FromArgb(250, 249, 253); collection.AccessibleName = "灵域收藏"; content.Controls.Add(collection);
            content.Controls.Add(Line("满足道途或境界条件即可进入；秘境只生成虚拟妖物，不处理真实文件、进程或内存。收起窗口会继续战斗；要中止本次探索，请用「暂离蜃楼」，重访后继续。", 58));
            content.Controls.Add(directory);
            FlowLayoutPanel tail = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(12, 6, 12, 6) };
            Configure(continueExpedition, "进入千面蜃楼", 180, delegate { controller.LingyuEnter("qianmian"); RefreshData(); });
            continueExpedition.Name = "continue-lingyu";
            Configure(suspendExpedition, "暂离蜃楼 · 保留进度", 180, delegate { controller.LingyuSuspend(); RefreshData(); });
            suspendExpedition.Name = "suspend-lingyu";
            Configure(closePanel, "收起灵域", 140, delegate { Hide(); });
            tail.Controls.Add(continueExpedition); tail.Controls.Add(suspendExpedition); tail.Controls.Add(closePanel); Controls.Add(tail);

            refresh.Interval = 500; refresh.Tick += delegate { if (Visible) RefreshData(); }; refresh.Start();
            Shown += delegate { RefreshData(); };
            if (controller.IsolatedDesktop) DesktopFeatureStyle.Apply(this);
            DesktopFeatureStyle.BindResponsiveColumn(content);
        }

        private Label Line(string text, int height) { return new Label { Text = text, Width = 640, Height = height, AutoSize = true, MaximumSize = new Size(640, 0), MinimumSize = new Size(0, height), Margin = new Padding(0, 4, 0, 4) }; }
        private FlowLayoutPanel Row() { return new FlowLayoutPanel { Width = 650, Height = 40, AutoSize = true, MinimumSize = new Size(0, 40), WrapContents = true, Margin = new Padding(0, 3, 0, 3) }; }
        private void Configure(Button button, string text, int width, Action action) { button.Text = text; button.Width = width; button.Height = 34; button.FlatStyle = FlatStyle.Flat; button.BackColor = Color.FromArgb(115, 78, 191); button.ForeColor = Color.White; button.FlatAppearance.BorderSize = 0; button.Cursor = Cursors.Hand; button.Click += delegate { if (!updating) action(); }; }

        internal void RefreshData()
        {
            if (IsDisposed) return; updating = true;
            try
            {
                LingyuProgress progress = controller.Lingyu;
                MirageEngine mirage = controller.Mirage;
                bool canExplore = controller.LingyuCanExplore;
                continueExpedition.Visible = !progress.ExpeditionActive;
                continueExpedition.Enabled = canExplore && controller.LingyuCanEnter(LingyuCatalog.FindRealm("qianmian"));
                continueExpedition.Text = progress.ExpeditionNode.Length > 0 ? "继续蜃楼之行" : "进入千面蜃楼";
                suspendExpedition.Visible = progress.ExpeditionActive;
                suspendExpedition.Enabled = controller.LingyuCanSuspend;
                for (int i = 0; i < LingyuCatalog.Realms.Length; i++)
                {
                    LingyuRealmDef realm = LingyuCatalog.Realms[i];
                    bool entered = progress.RealmEntered.Contains(realm.Id);
                    int visits = progress.Visits(realm.Id);
                    if (!realm.Playable)
                    {
                        realmStates[i].Text = "后续版本开放 · " + realm.EntryHint;
                        realmEnters[i].Enabled = false; realmEnters[i].Text = "未开放";
                    }
                    else if (!controller.LingyuCanEnter(realm))
                    {
                        realmStates[i].Text = "未解锁 · " + realm.EntryHint;
                        realmEnters[i].Enabled = false; realmEnters[i].Text = "进入";
                    }
                    else
                    {
                        bool journey = realm.Id == "qianmian" && (progress.ExpeditionActive || progress.ExpeditionNode.Length > 0);
                        realmStates[i].Text = visits > 0 ? "已通关 " + visits + " 次 · 可再探" : (entered ? "已发现 · 可进入" : "可进入");
                        realmEnters[i].Enabled = canExplore && !progress.ExpeditionActive; realmEnters[i].Text = journey ? "继续探索" : "进入蜃楼";
                    }
                }

                bool mirageKnown = progress.RealmEntered.Contains("qianmian");
                if (!mirageKnown)
                {
                    nodeTitle.Text = "尚未踏入任何灵域";
                    nodeText.Text = LingyuCatalog.FindRealm("qianmian").EntryHint + "\n满足条件后，点击下方「进入千面蜃楼」。镜像妖物与机缘藏于其中。";
                    expeditionHint.Text = "";
                    for (int i = 0; i < choices.Length; i++) choices[i].Visible = false;
                }
                else if (!progress.ExpeditionActive)
                {
                    nodeTitle.Text = "蜃楼之行暂存 · " + MirageEngine.NodeTitle(progress.ExpeditionNode);
                    nodeText.Text = progress.CombatPending ? "镜妖之战待续。点击下方「继续蜃楼之行」重新迎战；探索节点和机缘选择保留。"
                        : "点击下方「继续蜃楼之行」重访当前节点；尚待抉择的机缘会继续保留。";
                    expeditionHint.Text = "";
                    for (int i = 0; i < choices.Length; i++) choices[i].Visible = false;
                }
                else
                {
                    // 界面恢复兜底：战斗未决而桌面镜妖缺失（重启/异常）时，
                    // 玩家打开本面板即触发补发；已有存活镜妖时不重复生成。
                    if (progress.CombatPending) controller.TryResumeLingyuCombat();
                    nodeTitle.Text = "千面蜃楼 · " + MirageEngine.NodeTitle(mirage.NodeId) + " · 第 " + progress.ExpeditionDepth + " 重";
                    nodeText.Text = mirage.NodeText();
                    expeditionHint.Text = progress.CombatPending ? "镜妖正在桌面游荡——自动作战会迎击，也可以手动点击妖物出击。" : (mirage.PendingJiyuan != null ? "机缘当前，请作出抉择：" : "");
                    string[] options = mirage.CurrentChoices();
                    for (int i = 0; i < choices.Length; i++)
                    {
                        bool show = i < options.Length;
                        choices[i].Visible = show;
                        if (show) choices[i].Text = options[i];
                        choices[i].Enabled = canExplore;
                    }
                }
                if (controller.LingyuPersistencePending) expeditionHint.Text = "档案暂未保存，请保持程序运行等待重试；尚不能把这次进度视为已落盘。";
                else if (!canExplore && controller.LingyuRestSeconds > 0) expeditionHint.Text = "修士正在调息，约" + controller.LingyuRestSeconds + "秒后恢复。可继续阅读或暂离，恢复后再作探索抉择。";
                else if (progress.ExpeditionActive && !controller.LingyuCanSuspend) expeditionHint.Text = "当前战斗正在结算，结算后可暂离。";

                log.Items.Clear();
                for (int i = progress.ExpeditionLog.Count - 1; i >= 0; i--) log.Items.Add(progress.ExpeditionLog[i]);
                if (log.Items.Count == 0) log.Items.Add("（尚无探索记录）");

                collection.Items.Clear();
                foreach (System.Collections.Generic.KeyValuePair<string, int> pair in progress.Materials)
                    collection.Items.Add("材料 · " + LingyuCatalog.MaterialName(pair.Key) + " ×" + pair.Value.ToString(CultureInfo.InvariantCulture));
                foreach (System.Collections.Generic.KeyValuePair<string, int> pair in progress.Scrolls)
                    collection.Items.Add("功法残卷 · " + LingyuCatalog.ScrollName(pair.Key) + (pair.Value > 1 ? " ×" + pair.Value.ToString(CultureInfo.InvariantCulture) : ""));
                foreach (string heritage in progress.Heritages)
                    collection.Items.Add("传承 · " + LingyuCatalog.HeritageName(heritage));
                if (collection.Items.Count == 0) collection.Items.Add("（尚无收藏：蜃楼宝箱、镜妖与机缘会赠予材料、残卷与传承）");
            }
            finally { updating = false; }
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } base.OnFormClosing(e); }
        protected override void Dispose(bool disposing) { if (disposing) { refresh.Stop(); refresh.Dispose(); } base.Dispose(disposing); }
    }
}
