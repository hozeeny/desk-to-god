using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NetBunny
{
    // An on-demand companion panel. Actors and cultivation keep their own
    // desktop lifetime; this form never owns a second game or a scene canvas.
    internal sealed class DesktopPracticePanel : Form
    {
        readonly DesktopDemoSession session;
        readonly Label objective = new Label(), stats = new Label(), hint = new Label(), notice = new Label();
        readonly Label attackState = new Label(), blockState = new Label(), cultivation = new Label(), weaponInfo = new Label(), techniqueInfo = new Label();
        readonly Label weaponProgress = new Label();
        readonly PictureBox weaponPreview = new PictureBox();
        readonly FlowLayoutPanel weaponPreviewRow = new FlowLayoutPanel();
        readonly Button equipWeapon = new Button();
        readonly Button strike = new Button(), block = new Button(), awaken = new Button(), automatic = new Button(), practice = new Button();
        readonly Button nextAction = new Button();
        readonly FlowLayoutPanel returnTasks = new FlowLayoutPanel();
        readonly Button[] returnTaskButtons = { new Button(), new Button() };
        readonly Label[] returnTaskLabels = { new Label(), new Label() };
        readonly Action<string> returnTaskNavigate;
        readonly FlowLayoutPanel combatActions = new FlowLayoutPanel();
        readonly ComboBox weapons = new ComboBox(), techniques = new ComboBox();
        readonly FlowLayoutPanel targets = new FlowLayoutPanel(), materials = new FlowLayoutPanel();
        readonly FlowLayoutPanel screenChoices = new FlowLayoutPanel();
        readonly Dictionary<string, MaterialCard> materialCards = new Dictionary<string, MaterialCard>();
        readonly Dictionary<string, Button> screenButtons = new Dictionary<string, Button>();
        Label emptyMaterials;
        DateTime nextScreenRefresh;
        readonly TextBox activity = new TextBox();
        readonly TabControl tabs = new TabControl();
        readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
        readonly List<FlowLayoutPanel> columns = new List<FlowLayoutPanel>();
        readonly Dictionary<string, Button> targetButtons = new Dictionary<string, Button>();
        readonly List<Image> ownedImages = new List<Image>();
        readonly DesktopProgressMeter meter = new DesktopProgressMeter();
        string inventoryKey = "";
        bool welcomeWithoutActivation;
        static readonly Color Ink = Color.FromArgb(38, 65, 58), Paper = Color.FromArgb(246, 242, 230), Jade = Color.FromArgb(41, 91, 75), Gold = Color.FromArgb(133, 91, 41);

        internal DesktopPracticePanel(DesktopDemoSession session)
        {
            this.session = session;
            returnTaskNavigate = session.OpenPanel;
            Text = "桌面修仙传 · 修行手札";
            Font = new Font("Microsoft YaHei UI", 10);
            BackColor = Paper; ForeColor = Ink; AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.Manual;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(470, work.Width - 32), Math.Min(700, work.Height - 72));
            MinimumSize = new Size(Math.Min(390, work.Width - 24), Math.Min(490, work.Height - 24));
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(14) };
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(shell);
            var summary = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            objective.Font = new Font("Microsoft YaHei UI", 13, FontStyle.Bold); objective.ForeColor = Jade; StyleLabel(objective);
            StyleLabel(stats); stats.Margin = new Padding(0, 5, 0, 8);
            meter.Height = 5; meter.Width = 420; meter.Margin = new Padding(0, 0, 0, 5);
            StyleLabel(notice); notice.ForeColor = Gold; notice.AccessibleName = "战斗结果";
            Configure(nextAction, "下一步", delegate
            {
                string id = session.Game.ReadDesktopPlayerView().NextActionId;
                if (id == "awaken") session.Game.AwakenSpiritRoot();
                else if (id == "training" || id == "lingyu" || id == "dongtian" || id == "breakthrough") session.OpenPanel(id);
            });
            nextAction.AccessibleName = "当前成长行动";
            nextAction.Width = 420;
            summary.Controls.Add(objective); summary.Controls.Add(stats); summary.Controls.Add(notice); summary.Controls.Add(meter); summary.Controls.Add(nextAction); shell.Controls.Add(summary, 0, 0);
            tabs.Dock = DockStyle.Fill; tabs.SizeMode = TabSizeMode.Fixed; tabs.ItemSize = new Size(108, 30); tabs.Margin = Padding.Empty;
            shell.Controls.Add(tabs, 0, 1);

            FlowLayoutPanel battle = Column("桌面历练");
            StyleLabel(hint); hint.AccessibleName = "下一步操作"; battle.Controls.Add(hint);
            returnTasks.AutoSize = true; returnTasks.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            returnTasks.FlowDirection = FlowDirection.TopDown; returnTasks.WrapContents = false; returnTasks.Margin = Padding.Empty;
            for (int i = 0; i < returnTaskButtons.Length; i++)
            {
                string route = i == 0 ? "lingyu" : "dongtian"; Button button = returnTaskButtons[i]; Label text = returnTaskLabels[i];
                Configure(button, "", delegate { returnTaskNavigate(route); });
                button.Name = "return-task-" + route; button.Tag = route; button.Width = 92; button.Margin = Padding.Empty;
                button.ForeColor = Jade; button.UseMnemonic = false;
                StyleLabel(text); text.Name = "return-task-text-" + route; text.Margin = new Padding(0, 5, 8, 0);
                var row = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 8) };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
                row.Controls.Add(text, 0, 0); row.Controls.Add(button, 1, 0);
                button.Visible = false; row.Visible = false; returnTasks.Controls.Add(row);
            }
            returnTasks.Visible = false; battle.Controls.Add(returnTasks);
            Add(battle, "电脑生态 · 查看来源与维护结果", delegate { session.OpenPanel("ecology"); });
            StyleLabel(attackState); StyleLabel(blockState); battle.Controls.Add(attackState); battle.Controls.Add(blockState);
            combatActions.AutoSize = true; combatActions.AutoSizeMode = AutoSizeMode.GrowAndShrink; combatActions.WrapContents = true; combatActions.Margin = Padding.Empty;
            Configure(strike, "对选中妖物出招", delegate { session.Game.DesktopStrikeSelected(); }); combatActions.Controls.Add(strike);
            Configure(block, "格挡 · 点击修士", delegate { session.Game.TryBlock(); }); combatActions.Controls.Add(block); battle.Controls.Add(combatActions);
            Configure(awaken, "灵根觉醒 · 十次有效击杀后", delegate { session.Game.AwakenSpiritRoot(); }); battle.Controls.Add(awaken);
            Configure(automatic, "自动作战", delegate { session.Game.SetAutoPlan(!session.Game.AutoPlanning); }); battle.Controls.Add(automatic);
            Section(battle, "桌面妖物 · 选中后查看来源");
            targets.AutoSize = false; targets.Height = 170; targets.AutoScroll = true; targets.FlowDirection = FlowDirection.TopDown; targets.WrapContents = false; battle.Controls.Add(targets);
            Section(battle, "历练记事");
            activity.ReadOnly = true; activity.Multiline = true; activity.Height = 104; activity.BorderStyle = BorderStyle.None;
            activity.BackColor = Color.FromArgb(237, 235, 221); activity.ForeColor = Ink; activity.ScrollBars = ScrollBars.Vertical; activity.TabStop = false;
            activity.AccessibleName = "最近六条历练记录"; battle.Controls.Add(activity);

            FlowLayoutPanel training = Column("修行与洞天");
            StyleLabel(cultivation); cultivation.AccessibleName = "境界与成长目标"; training.Controls.Add(cultivation);
            Configure(practice, "修炼", delegate { session.Game.SetCultivationPaused(!session.Game.CultivationPaused); }); training.Controls.Add(practice);
            Section(training, "功法 · 觉醒后可随时改修");
            techniques.DropDownStyle = ComboBoxStyle.DropDownList; techniques.AccessibleName = "修行功法";
            foreach (CultivationTechnique technique in CultivationTechnique.All) techniques.Items.Add(new Choice { Id = technique.Id, Text = technique.Name });
            techniques.SelectedIndexChanged += delegate { UpdateTechniqueDescription(); }; training.Controls.Add(techniques);
            StyleLabel(techniqueInfo); training.Controls.Add(techniqueInfo);
            Add(training, "改修所选功法", delegate { Choice choice = techniques.SelectedItem as Choice; if (choice != null && !session.Game.Cultivation.Mortal) session.Game.ChooseTechnique(choice.Id); }).Name = "change-technique";
            Section(training, "在世修行");
            Add(training, "百业道碑 · 工作与生活的后天道途", delegate { session.OpenPanel("baiye"); });
            Add(training, "灵域历练 · 探索秘境与机缘", delegate { session.OpenPanel("lingyu"); });
            Add(training, "破境 · 查看圆满与契机", delegate { session.OpenPanel("breakthrough"); });
            Add(training, "洞天 · 筑基后种植、炼丹与铸器", delegate { session.OpenPanel("dongtian"); });
            var privacy = TextLine("百业默认关闭；由你在道碑中选择是否开启。\n收起所有面板，修士仍会在桌面继续修炼。"); training.Controls.Add(privacy);

            FlowLayoutPanel bag = Column("行囊与设置");
            Section(bag, "已有兵器"); StyleLabel(weaponProgress); bag.Controls.Add(weaponProgress);
            weapons.DropDownStyle = ComboBoxStyle.DropDownList; weapons.AccessibleName = "已拥有兵器";
            weapons.SelectedIndexChanged += delegate { UpdateWeaponDescription(); }; bag.Controls.Add(weapons);
            weaponPreviewRow.AutoSize = true; weaponPreviewRow.AutoSizeMode = AutoSizeMode.GrowAndShrink; weaponPreviewRow.WrapContents = false; weaponPreviewRow.Margin = Padding.Empty;
            weaponPreview.Size = new Size(64, 64); weaponPreview.SizeMode = PictureBoxSizeMode.Zoom; weaponPreview.Margin = new Padding(0, 0, 10, 6); weaponPreview.AccessibleName = "所选兵器原画";
            StyleLabel(weaponInfo); weaponPreviewRow.Controls.Add(weaponPreview); weaponPreviewRow.Controls.Add(weaponInfo); bag.Controls.Add(weaponPreviewRow);
            Configure(equipWeapon, "装备所选兵器", delegate { Choice choice = weapons.SelectedItem as Choice; if (choice != null) session.Game.ChooseWeapon(choice.Id); });
            equipWeapon.Name = "equip-weapon"; bag.Controls.Add(equipWeapon);
            Section(bag, "材料与洞天储物");
            materials.AutoSize = true; materials.FlowDirection = FlowDirection.TopDown; materials.WrapContents = false; bag.Controls.Add(materials);
            Section(bag, "桌面位置");
            screenChoices.AutoSize = true; screenChoices.FlowDirection = FlowDirection.TopDown; screenChoices.WrapContents = false; screenChoices.Margin = Padding.Empty;
            bag.Controls.Add(screenChoices);
            Add(bag, "隐藏／恢复桌面角色", delegate { session.Game.DesktopSetActorsVisible(!session.Game.DesktopActorsVisible); });
            bag.Controls.Add(TextLine("拖动修士可以移动；右键修士可再次打开手札。\n独立进度每20秒保存，正常退出时再次保存。"));

            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 10, 0, 0) };
            Button hide = new Button { Text = "收起 · 继续桌面修仙", Width = 245, Height = 34 }; StyleButton(hide); hide.Click += delegate { Hide(); };
            Button exit = new Button { Text = "保存并退出", Width = 160, Height = 34 }; StyleButton(exit); exit.Click += delegate { session.ExitAfterAcceptance(0); };
            footer.Controls.Add(hide); footer.Controls.Add(exit); shell.Controls.Add(footer, 0, 2);
            Resize += delegate
            {
                int width = Math.Max(280, shell.ClientSize.Width - shell.Padding.Horizontal - 4);
                objective.MaximumSize = stats.MaximumSize = new Size(width, 0); meter.Width = nextAction.Width = width;
                hide.Width = Math.Max(160, (width - 12) * 3 / 5); exit.Width = Math.Max(100, width - hide.Width - 12);
                ResizeColumns();
            };
            refresh.Interval = 250; refresh.Tick += delegate { if (Visible) UpdateState(); }; refresh.Start();
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            Shown += delegate { ResizeColumns(); };
        }

        protected override bool ShowWithoutActivation { get { return welcomeWithoutActivation; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams parameters = base.CreateParams; if (welcomeWithoutActivation) parameters.ExStyle |= 8; return parameters; }
        }
        internal void ShowWelcome() { welcomeWithoutActivation = true; try { UpdateState(); Show(); } finally { welcomeWithoutActivation = false; } }
        internal void OpenSection(string id) { tabs.SelectedIndex = id == "training" ? 1 : id == "inventory" ? 2 : 0; }
        internal void UpdateState()
        {
            AdventureController game = session.Game; if (game == null) return;
            DesktopPlayerView view = game.ReadDesktopPlayerView();
            var c = game.Cultivation; var p = game.Battle.Progress;
            objective.Text = view.Objective;
            stats.Text = "气血 " + p.Health + "/" + p.MaxHealth + (c.Mortal ? "　历练积累 " + c.DeferredQi : "　" + c.RootName + (c.RealmComplete ? " · 圆满" : ""));
            meter.Value = c.Mortal ? c.MortalKills / 10.0 : c.RealmComplete ? 1 : c.Qi / (double)Math.Max(1, c.NextLayerCost);
            hint.Text = view.NextStep; hint.AccessibleDescription = view.Hint;
            nextAction.Text = view.NextActionText; nextAction.Visible = !String.IsNullOrEmpty(view.NextActionId);
            UpdateReturnTasks(view.ReturnTasks);
            attackState.Text = "目标：" + view.TargetName + "\n" + view.AttackText;
            blockState.Text = view.BlockText;
            strike.Enabled = view.StrikeReady; block.Enabled = view.BlockReady;
            awaken.Enabled = c.CanAwaken; awaken.Text = c.CanAwaken ? "灵根觉醒 · 十战已满，引气入体" : c.Mortal ? "灵根觉醒 · 历练 " + c.MortalKills + "/10" : "灵根已觉醒 · " + c.RootName;
            awaken.Visible = c.CanAwaken;
            automatic.Enabled = c.AutoBattleUnlocked && !p.Dead;
            automatic.Visible = c.AutoBattleUnlocked;
            automatic.Text = c.AutoBattleUnlocked ? "自动作战：" + (game.AutoPlanning ? "开启 · 自主索敌" : "关闭 · 手动历练") : "自动作战 · 灵根觉醒后开启";
            notice.Text = view.Notice;
            string events = String.Join(Environment.NewLine, view.RecentEvents ?? new string[0]); if (activity.Text != events) activity.Text = events;
            UpdateTargets(view.Enemies ?? new DesktopEnemyChoice[0]);
            cultivation.Text = c.Mortal ? "凡躯尚不能存纳灵气。\n十次有效击杀后，主动觉醒灵根；历练积累将转为修为。"
                : c.RootName + " · " + CultivationIdentity.PathName(c.DaoPath) + "\n正在修习「" + c.Technique.Name + "」\n" + game.CultivationLine
                + "\n" + (c.RealmComplete ? "本境已圆满。打开破境查看契机、材料与成功率。" : "下层目标：" + c.NextLayerCost + " 修为；吐纳与战斗均可积累。")
                + "\n百业可积累后天道途，灵域带来机缘；筑基后开启洞天经营。";
            practice.Enabled = !c.Mortal; practice.Text = c.Mortal ? "吐纳修炼 · 觉醒后开启" : "吐纳修炼：" + (game.CultivationPaused ? "暂停，点击继续" : "持续进行，点击暂歇");
            techniques.Enabled = !c.Mortal;
            foreach (Control control in columns[1].Controls) if (control.Name == "change-technique") control.Enabled = !c.Mortal;
            if (techniques.SelectedIndex < 0) techniques.SelectedIndex = Array.FindIndex(CultivationTechnique.All, t => t.Id == c.TechniqueId);
            string key = String.Join("|", p.Inventory.ToArray()) + ":" + p.WeaponId;
            if (key != inventoryKey)
            {
                Choice selected = weapons.SelectedItem as Choice;
                string previewId = selected != null && p.Inventory.Contains(selected.Id) ? selected.Id : p.WeaponId;
                inventoryKey = key; weapons.Items.Clear();
                foreach (string id in p.Inventory) weapons.Items.Add(new Choice { Id = id, Text = WeaponName(id) + (id == p.WeaponId ? " · 已装备" : "") });
                for (int i = 0; i < weapons.Items.Count; i++) if (((Choice)weapons.Items[i]).Id == previewId) weapons.SelectedIndex = i;
                if (weapons.SelectedIndex < 0 && weapons.Items.Count > 0) weapons.SelectedIndex = 0;
            }
            weaponProgress.Text = DesktopWeaponCatalog.ProgressText(p);
            UpdateWeaponDescription();
            UpdateMaterials(); UpdateScreens(); ResizeColumns();
        }

        void UpdateTargets(DesktopEnemyChoice[] enemies)
        {
            var ids = new HashSet<string>(enemies.Select(e => e.Id));
            foreach (string id in targetButtons.Keys.Where(id => !ids.Contains(id)).ToArray()) { Button b = targetButtons[id]; targets.Controls.Remove(b); b.Dispose(); targetButtons.Remove(id); }
            foreach (DesktopEnemyChoice enemy in enemies)
            {
                Button button;
                if (!targetButtons.TryGetValue(enemy.Id, out button))
                {
                    string id = enemy.Id; button = new Button { Height = 78, Margin = new Padding(0, 0, 0, 5) }; StyleButton(button);
                    button.Click += delegate { session.Game.DesktopChooseEnemy(id, false); UpdateState(); };
                    targetButtons.Add(id, button); targets.Controls.Add(button);
                }
                button.Text = (!enemy.CanSelect ? "提示 · " : enemy.Selected ? "当前目标 · " : "选中 · ") + enemy.Name + (enemy.CanSelect ? "　" + enemy.Health + "/" + enemy.MaxHealth : "") + "\n" + enemy.Source + "\n" + enemy.Action;
                button.BackColor = enemy.Selected ? Color.FromArgb(221, 232, 214) : Color.FromArgb(251, 248, 239);
                button.Enabled = enemy.CanSelect;
                button.Width = Math.Max(220, targets.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            }
            targets.Height = Math.Max(32, Math.Min(170, enemies.Length * 83));
        }
        void UpdateWeaponDescription()
        {
            Choice choice = weapons.SelectedItem as Choice;
            if (choice == null) { equipWeapon.Enabled = false; weaponInfo.Text = ""; weaponPreview.Image = null; return; }
            AdventureWeapon w = AdventureWeapon.Find(choice.Id);
            DesktopWeaponInfo info = DesktopWeaponCatalog.Find(choice.Id);
            bool equipped = session.Game.Battle.Progress.WeaponId == choice.Id;
            weaponInfo.Text = info.Name + (equipped ? " · 已装备" : " · 预览") + "\n基础威力 " + w.Damage + " · 出招间隔 " + w.Cooldown.ToString("0.00") + "秒\n"
                + info.Role + (w.Area ? " · 群攻战斗队列中的妖物" : " · 单体出招") + "\n实际伤害另计境界、历练和器物增益。";
            weaponPreview.Image = Thumbnail(info.ImageFile);
            equipWeapon.Text = equipped ? "已装备「" + info.Name + "」" : "装备「" + info.Name + "」";
            equipWeapon.Enabled = !equipped && session.Game.Battle.Progress.Inventory.Contains(choice.Id);
        }
        void UpdateTechniqueDescription()
        { Choice choice = techniques.SelectedItem as Choice; if (choice != null) techniqueInfo.Text = CultivationTechnique.Find(choice.Id).Description; }
        void UpdateMaterials()
        {
            DesktopMaterialInfo[] rows = DesktopMaterialCatalog.Read(session.Game);
            var keys = new HashSet<string>(rows.Select(row => row.Key));
            bool changed = materialCards.Keys.Any(key => !keys.Contains(key)) || rows.Any(row => !materialCards.ContainsKey(row.Key));
            Point scroll = columns[2].AutoScrollPosition;
            materials.SuspendLayout();
            try
            {
                foreach (string key in materialCards.Keys.Where(key => !keys.Contains(key)).ToArray())
                { DisposeRetiredControl(materialCards[key]); materialCards.Remove(key); }
                if (rows.Length == 0)
                {
                    if (emptyMaterials == null) emptyMaterials = TextLine("行囊暂空。灵域收藏与洞天仓储在这里分列，获得材料后可查看真实用途。普通战斗先积累历练与修为。");
                    if (emptyMaterials.Parent != materials) materials.Controls.Add(emptyMaterials);
                }
                else
                {
                    if (emptyMaterials != null && emptyMaterials.Parent == materials) materials.Controls.Remove(emptyMaterials);
                    for (int i = 0; i < rows.Length; i++)
                    {
                        DesktopMaterialInfo row = rows[i]; MaterialCard card;
                        if (!materialCards.TryGetValue(row.Key, out card))
                        { card = new MaterialCard(this, row.Key); materialCards.Add(row.Key, card); materials.Controls.Add(card); }
                        card.UpdateInfo(row);
                        if (materials.Controls.GetChildIndex(card) != i) materials.Controls.SetChildIndex(card, i);
                    }
                }
            }
            finally { materials.ResumeLayout(true); }
            if (changed) columns[2].AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
        }
        void UpdateScreens()
        {
            if (DateTime.UtcNow < nextScreenRefresh) return;
            nextScreenRefresh = DateTime.UtcNow.AddSeconds(1);
            Screen[] screens = Screen.AllScreens;
            var ids = new HashSet<string>(screens.Select(screen => screen.DeviceName));
            foreach (string id in screenButtons.Keys.Where(id => !ids.Contains(id)).ToArray())
            { DisposeRetiredControl(screenButtons[id]); screenButtons.Remove(id); }
            for (int i = 0; i < screens.Length; i++)
            {
                Screen screen = screens[i]; Button button;
                if (!screenButtons.TryGetValue(screen.DeviceName, out button))
                {
                    string deviceName = screen.DeviceName;
                    button = Add(screenChoices, "", delegate
                    {
                        Screen current = Screen.AllScreens.FirstOrDefault(candidate => candidate.DeviceName == deviceName);
                        if (current != null) session.Game.ChangeScreen(current);
                    });
                    button.Tag = deviceName; screenButtons.Add(deviceName, button);
                }
                button.Text = "移至屏幕 " + (i + 1) + (screen.Primary ? " · 主屏" : "");
                button.BackColor = screen.WorkingArea == session.Game.WorkingArea ? Color.FromArgb(221, 232, 214) : Color.FromArgb(251, 248, 239);
                if (screenChoices.Controls.GetChildIndex(button) != i) screenChoices.Controls.SetChildIndex(button, i);
            }
        }
        sealed class MaterialCard : FlowLayoutPanel
        {
            readonly DesktopPracticePanel owner;
            readonly FlowLayoutPanel header = new FlowLayoutPanel();
            readonly PictureBox icon = new PictureBox();
            readonly Label title = TextLine(""), location = TextLine(""), purpose = TextLine("");
            Button action;
            internal MaterialCard(DesktopPracticePanel owner, string key)
            {
                this.owner = owner; Name = "material-" + key;
                AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; FlowDirection = FlowDirection.TopDown; WrapContents = false;
                Padding = new Padding(8); Margin = new Padding(0, 0, 0, 9); BorderStyle = BorderStyle.FixedSingle; BackColor = Color.FromArgb(251, 248, 239);
                header.AutoSize = true; header.AutoSizeMode = AutoSizeMode.GrowAndShrink; header.WrapContents = false; header.Margin = Padding.Empty;
                icon.Size = new Size(30, 30); icon.SizeMode = PictureBoxSizeMode.Zoom; icon.Margin = new Padding(0, 0, 8, 4);
                title.ForeColor = Jade; title.Margin = new Padding(0, 4, 0, 4);
                location.ForeColor = Gold; location.Margin = new Padding(0, 0, 0, 5);
                header.Controls.Add(icon); header.Controls.Add(title); Controls.Add(header); Controls.Add(location); Controls.Add(purpose);
            }
            internal void UpdateInfo(DesktopMaterialInfo info)
            {
                SuspendLayout();
                try
                {
                    string text = info.Name + " × " + info.Count;
                    if (title.Text != text) title.Text = text;
                    if (location.Text != info.Location) location.Text = info.Location;
                    if (purpose.Text != info.Purpose) purpose.Text = info.Purpose;
                    icon.Image = owner.Thumbnail(info.Image); icon.Visible = icon.Image != null;
                    if (String.IsNullOrEmpty(info.ActionId)) { if (action != null) { owner.DisposeRetiredControl(action); action = null; } }
                    else
                    {
                        if (action == null)
                        {
                            action = new Button { Name = "material-action-" + info.Key };
                            owner.Configure(action, "", delegate
                            {
                                string id = action.Tag as string;
                                if (id != "breakthrough" && id != "dongtian" && id != "lingyu") return;
                                owner.session.OpenPanel(id);
                                if (id == "lingyu")
                                {
                                    LingyuForm collection = Application.OpenForms.OfType<LingyuForm>().FirstOrDefault(form => form.Visible);
                                    if (collection != null) collection.ShowCollection();
                                }
                            });
                            Controls.Add(action);
                        }
                        action.Tag = info.ActionId; action.Text = info.ActionText;
                    }
                }
                finally { ResumeLayout(true); }
            }
            internal void Fit(int width)
            {
                MinimumSize = new Size(width, 0); MaximumSize = new Size(width, 0); Width = width;
                int inner = Math.Max(1, ClientSize.Width - Padding.Horizontal);
                header.MinimumSize = new Size(inner, 0); header.MaximumSize = new Size(inner, 0); header.Width = inner;
                title.MaximumSize = new Size(Math.Max(1, inner - icon.Width - icon.Margin.Horizontal), 0);
                location.MaximumSize = purpose.MaximumSize = new Size(inner, 0);
                location.MinimumSize = purpose.MinimumSize = new Size(inner, 0);
                if (action != null) action.Width = inner;
            }
        }
        void DisposeRetiredControl(Control control)
        {
            // WinForms remembers the last active child even after another form
            // receives focus. Removing that child calls SetFocus on a sibling;
            // clear only the stale background selection before retiring it.
            Control active = ActiveControl;
            if (!ContainsFocus && active != null && (active == control || control.Contains(active))) ActiveControl = null;
            control.Dispose();
        }
        Image Thumbnail(string image)
        {
            Image thumbnail = ownedImages.FirstOrDefault(i => (string)i.Tag == image);
            if (thumbnail != null) return thumbnail;
            string root = Environment.GetEnvironmentVariable(ArtDemoPack.EnvironmentVariable);
            if (String.IsNullOrEmpty(root)) root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArtDemoAssets");
            string path = Path.Combine(root, image); if (!File.Exists(path)) return null;
            using (Image source = Image.FromFile(path)) thumbnail = new Bitmap(source);
            thumbnail.Tag = image; ownedImages.Add(thumbnail); return thumbnail;
        }
        void UpdateReturnTasks(DesktopReturnTask[] tasks)
        {
            for (int i = 0; i < returnTaskButtons.Length; i++)
            {
                Button button = returnTaskButtons[i];
                DesktopReturnTask task = tasks.FirstOrDefault(t => t.PanelId == (string)button.Tag);
                if (task == null && !ContainsFocus && ActiveControl == button) ActiveControl = null;
                button.Visible = button.Parent.Visible = task != null;
                if (task == null) continue;
                button.Text = task.ActionText; returnTaskLabels[i].Text = task.Text;
                button.AccessibleName = task.ActionText; button.AccessibleDescription = task.Text;
            }
            returnTasks.Visible = tasks.Length > 0;
        }
        FlowLayoutPanel Column(string title)
        {
            var page = new TabPage(title) { BackColor = Paper, Padding = new Padding(10) }; tabs.TabPages.Add(page);
            var column = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            page.Controls.Add(column); columns.Add(column); return column;
        }
        void ResizeColumns()
        {
            foreach (var column in columns)
            {
                int width = Math.Max(240, column.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 16);
                foreach (Control control in column.Controls) { control.Width = width; if (control is Label) control.MaximumSize = new Size(width, 0); }
                foreach (Control control in targets.Controls) control.Width = width;
                if (column == columns[0])
                    for (int i = 0; i < returnTaskButtons.Length; i++)
                    {
                        returnTaskButtons[i].Parent.Width = width;
                        returnTaskLabels[i].MaximumSize = new Size(Math.Max(1, width - 100), 0);
                    }
                if (column == columns[2])
                {
                    foreach (Control control in materials.Controls)
                    { MaterialCard card = control as MaterialCard; if (card != null) card.Fit(width); else { control.Width = width; if (control is Label) control.MaximumSize = new Size(width, 0); } }
                    foreach (Control control in screenChoices.Controls) control.Width = width;
                }
                strike.Width = Math.Max(115, (width - 8) / 2); block.Width = strike.Width;
                strike.Margin = new Padding(0, 0, 6, 6);
                foreach (Control control in targets.Controls) control.Width = Math.Max(220, width - SystemInformation.VerticalScrollBarWidth - 4);
                weaponInfo.MaximumSize = new Size(Math.Max(150, width - weaponPreview.Width - weaponPreview.Margin.Horizontal - weaponInfo.Margin.Horizontal - 2), 0);
            }
        }
        static void StyleLabel(Label label) { label.AutoSize = true; label.Margin = new Padding(0, 0, 0, 8); label.MaximumSize = new Size(400, 0); }
        static Label TextLine(string text) { var label = new Label { Text = text }; StyleLabel(label); return label; }
        static void Section(FlowLayoutPanel parent, string text) { var label = TextLine(text); label.Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold); label.Margin = new Padding(0, 12, 0, 6); parent.Controls.Add(label); }
        Button Add(FlowLayoutPanel content, string text, Action action) { var button = new Button(); Configure(button, text, action); content.Controls.Add(button); return button; }
        void Configure(Button button, string text, Action action) { button.Text = text; button.Height = 35; button.Margin = new Padding(0, 0, 0, 6); StyleButton(button); button.Click += delegate { action(); UpdateState(); }; }
        static void StyleButton(Button button) { button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Color.FromArgb(154, 166, 142); button.BackColor = Color.FromArgb(251, 248, 239); button.ForeColor = Ink; button.Cursor = Cursors.Hand; }
        internal static string WeaponName(string id)
        { return DesktopWeaponCatalog.Find(id).Name; }
        sealed class Choice { internal string Id, Text; public override string ToString() { return Text; } }
        protected override void Dispose(bool disposing)
        { if (disposing) { refresh.Stop(); refresh.Dispose(); if (emptyMaterials != null) emptyMaterials.Dispose(); foreach (Image image in ownedImages) image.Dispose(); ownedImages.Clear(); } base.Dispose(disposing); }
    }
    internal sealed class DesktopProgressMeter : Control
    {
        double value;
        internal double Value { get { return value; } set { double next = Math.Max(0, Math.Min(1, value)); if (next != this.value) { this.value = next; Invalidate(); } } }
        internal DesktopProgressMeter() { DoubleBuffered = true; TabStop = false; }
        protected override void OnPaint(PaintEventArgs e)
        { e.Graphics.Clear(Color.FromArgb(216, 218, 199)); using (var brush = new SolidBrush(Color.FromArgb(56, 108, 83))) e.Graphics.FillRectangle(brush, 0, 0, (float)(Width * value), Height); }
    }
}
