using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace NetBunny
{
    internal sealed class DesktopEcologyPanel:Form
    {
        readonly AdventureController game;
        readonly Label status=new Label(),programPath=new Label();
        readonly Button files=new Button(),memory=new Button(),automatic=new Button(),scanPrograms=new Button();
        readonly ComboBox programs=new ComboBox();
        readonly TextBox results=new TextBox();
        readonly TextBox targetSource=new TextBox();
        readonly Timer refresh=new Timer();
        readonly FlowLayoutPanel content=new FlowLayoutPanel();
        internal DesktopEcologyPanel(AdventureController game)
        {
            this.game=game;Text="桌面修仙传 · 电脑生态与维护";Font=new Font("Microsoft YaHei UI",10);
            BackColor=Color.FromArgb(246,242,230);ForeColor=Color.FromArgb(38,65,58);
            ClientSize=new Size(510,700);MinimumSize=new Size(410,490);StartPosition=FormStartPosition.Manual;
            content.Dock=DockStyle.Fill;content.AutoScroll=true;content.FlowDirection=FlowDirection.TopDown;content.WrapContents=false;content.Padding=new Padding(16);Controls.Add(content);
            Label heading=Line("电脑负担化为妖物，处理结果如实记录");heading.Font=new Font(Font,FontStyle.Bold);
            status.AutoSize=true;content.Controls.Add(status);
            Line("当前目标 · 来源与拟执行动作",true);
            targetSource.Multiline=true;targetSource.ReadOnly=true;targetSource.Height=154;targetSource.ScrollBars=ScrollBars.Both;
            targetSource.WordWrap=false;targetSource.TabStop=false;targetSource.HideSelection=true;targetSource.BackColor=Color.FromArgb(251,248,239);targetSource.AccessibleName="当前妖物来源详情";content.Controls.Add(targetSource);
            Add("重新观察临时文件",()=>game.DesktopRefreshSources());
            Line("文件怪绑定本用户临时文件；内存 Boss 对应内存压力。未授权时，击败目标只获得修行奖励。",true);
            Configure(files,()=>Toggle("files"));Configure(memory,()=>Toggle("memory"));Configure(automatic,()=>Toggle("automatic"));
            Line("程序与程灵",true);
            Line("正在运行的程序不等于垃圾。由你选择待处理程序，击败程灵后再单独确认正常退出；不强制结束。",true);
            scanPrograms.Text="查看可正常退出的后台程序";Configure(scanPrograms,async()=>await ScanPrograms());
            programs.DropDownStyle=ComboBoxStyle.DropDownList;programs.AccessibleName="待选择程序";content.Controls.Add(programs);
            programPath.AutoSize=true;content.Controls.Add(programPath);programs.SelectedIndexChanged+=delegate{var p=programs.SelectedItem as ProgramChoice;programPath.Text=p==null?"":p.Identity.ExecutablePath;};
            Add("将所选程序标记为程灵",()=>{var p=programs.SelectedItem as ProgramChoice;if(p!=null&&!game.DesktopAddProcessTarget(p.Identity))MessageBox.Show(this,"目标已存在、身份待复核或场上已满。请稍后重试。","尚未生成程灵");});
            Add("取消当前程序退出等待",()=>game.DesktopCancelProcessAction());
            Line("实际维护记录",true);results.Multiline=true;results.ReadOnly=true;results.Height=160;results.ScrollBars=ScrollBars.Vertical;results.BackColor=Color.FromArgb(251,248,239);content.Controls.Add(results);
            Add("收起 · 继续桌面修行",()=>Hide());
            Resize+=delegate{ResizeContent();};Shown+=delegate{ResizeContent();UpdateState();};
            refresh.Interval=500;refresh.Tick+=delegate{if(Visible)UpdateState();};refresh.Start();
            FormClosing+=delegate(object s,FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
            UpdateState();ResizeContent();
        }
        Label Line(string text,bool detail=false){var l=new Label{Text=text,AutoSize=true,Margin=new Padding(0,detail?10:0,0,8)};content.Controls.Add(l);return l;}
        void Add(string text,Action action){var b=new Button{Text=text};Configure(b,action);}
        void Configure(Button button,Action action){button.Height=36;button.FlatStyle=FlatStyle.Flat;button.BackColor=Color.FromArgb(251,248,239);button.Margin=new Padding(0,0,0,7);button.Click+=delegate{action();UpdateState();};content.Controls.Add(button);}
        void ResizeContent(){int width=Math.Max(300,content.ClientSize.Width-content.Padding.Horizontal-20);foreach(Control c in content.Controls){c.Width=width;var l=c as Label;if(l!=null)l.MaximumSize=new Size(width,0);}}
        void Toggle(string scope)
        {
            bool enabled=scope=="files"?game.MaintenanceConsentCleanup:scope=="memory"?game.MaintenanceConsentMemory:game.MaintenanceConsentAutoMaintenance;
            if(!enabled)
            {
                string text=scope=="files"?"击败文件怪后，将删除绑定的本用户Temp中超过24小时且通过复核的文件。占用、变化或不符合规则的文件会跳过。"
                    :scope=="memory"?"击败内存Boss后，尝试整理同会话部分后台程序的工作集。不会结束进程，也不保证电脑变快；只报告实际观测。"
                    :"允许自动战斗执行已单独授权的文件清理或内存维护。此项不授权关闭程序；可以随时关闭。";
                if(MessageBox.Show(this,text+"\n\n是否开启这一项授权？","真实电脑维护",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            }
            bool ok=scope=="files"?game.SetCleanupConsent(!enabled):scope=="memory"?game.SetMemoryConsent(!enabled):game.SetAutoMaintenanceConsent(!enabled);
            if(!ok)MessageBox.Show(this,game.DesktopNotice,"授权尚未完成");
        }
        async Task ScanPrograms()
        {
            scanPrograms.Enabled=false;
            try
            {
                ProcessIdentity[] observed=await Task.Run(()=>DesktopProcessTargets.Observe());
                if(IsDisposed)return;
                programs.Items.Clear();foreach(var identity in observed)programs.Items.Add(new ProgramChoice{Identity=identity});
                if(programs.Items.Count>0)programs.SelectedIndex=0;else programPath.Text="当前没有可核实且支持正常退出的后台程序。";
            }
            catch(Exception e){if(!IsDisposed)programPath.Text="暂未完成观察："+e.Message;}
            finally{if(!IsDisposed)scanPrograms.Enabled=game.DesktopObservesSystem;}
        }
        internal void UpdateState()
        {
            var view=game.ReadDesktopPlayerView();status.Text=view.EcologyStatus+(game.DesktopProcessBusy?"\n正在等待程序正常退出":"");
            DesktopEnemyChoice selected=view.Enemies.FirstOrDefault(e=>e.Selected);
            string detail=(selected==null?"未选择目标":selected.Name)+"\n"+view.TargetSourceDetail+"\n\n拟执行："+view.TargetAction;
            // Native multiline EDIT requires CRLF. Keep volatile combat HP out
            // of this reader so damage ticks never reset its scroll/selection.
            detail=detail.Replace("\r\n","\n").Replace("\n",Environment.NewLine);
            if(targetSource.Text!=detail){targetSource.Text=detail;targetSource.Select(0,0);}
            bool enabled=game.DesktopObservesSystem;files.Enabled=memory.Enabled=automatic.Enabled=enabled;
            if(!enabled)scanPrograms.Enabled=false;
            files.Text="文件清理授权："+(game.MaintenanceConsentCleanup?"已开启 · 点击关闭":"未开启 · 点击了解范围");
            memory.Text="内存维护授权："+(game.MaintenanceConsentMemory?"已开启 · 点击关闭":"未开启 · 点击了解范围");
            automatic.Text="无人值守维护："+(game.MaintenanceConsentAutoMaintenance?"已开启 · 点击关闭":"未开启 · 自动战斗不代表维护授权");
            string text=String.Join(Environment.NewLine+Environment.NewLine,view.MaintenanceResults.Select(x=>x.Summary));
            if(String.IsNullOrEmpty(text))text="尚无真实维护记录。历练奖励不会换算成磁盘清理量。";
            if(results.Text!=text)results.Text=text;
        }
        sealed class ProgramChoice{internal ProcessIdentity Identity;public override string ToString(){return Identity.Name+" · PID "+Identity.Id+" · "+AdventureBattle.FormatBytes(Identity.WorkingSetBytes);}}
        protected override void Dispose(bool disposing){if(disposing){refresh.Stop();refresh.Dispose();}base.Dispose(disposing);}
    }
}
