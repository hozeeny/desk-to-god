using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NetBunny
{
    internal sealed class NetworkCenterForm : Form
    {
        private readonly TabControl pages=new TabControl();
        private readonly SpeedTestView speed;
        private readonly TrafficView traffic;
        private readonly NetworkToolsView tools;
        private bool shutdown;

        internal NetworkCenterForm(bool preview,TrafficLedger ledger,Func<List<AppInfo>> apps,Func<string> rules,Action<bool> activity)
        {
            Text="骑士助手 · 网络中心";Font=Art.Font(10);BackColor=Art.Bg;ForeColor=Art.Ink;
            try{Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{}
            AutoScaleMode=AutoScaleMode.None;ClientSize=new Size(1120,850);MinimumSize=new Size(920,680);StartPosition=FormStartPosition.CenterScreen;
            Rectangle screen=Screen.PrimaryScreen.WorkingArea;Height=Math.Min(Height,screen.Height-20);Width=Math.Min(Width,screen.Width-20);
            Panel header=new Panel{Dock=DockStyle.Top,Height=68,BackColor=Color.FromArgb(237,232,255)};
            header.Controls.Add(new Label{Text="网络中心",Font=Art.Font(20,FontStyle.Bold),ForeColor=Art.Ink,Location=new Point(22,14),Size=new Size(235,42)});
            header.Controls.Add(new Label{Text=preview?"示例数据 · 预览不会访问网络":"测速 · 今日用量 · 故障排查",Font=Art.Font(10),ForeColor=Art.Purple,Location=new Point(270,27),Size=new Size(580,30)});
            pages.Dock=DockStyle.Fill;pages.Font=Art.Font(11,FontStyle.Bold);pages.Padding=new Point(24,10);
            speed=new SpeedTestView(preview,apps,rules,activity){Dock=DockStyle.Fill};
            traffic=new TrafficView(preview,ledger){Dock=DockStyle.Fill};
            tools=new NetworkToolsView(preview){Dock=DockStyle.Fill};
            AddPage("测速与历史",speed);AddPage("流量排行与曲线",traffic);AddPage("网络工具",tools);
            Label footer=new Label{Dock=DockStyle.Bottom,Height=30,Padding=new Padding(20,7,0,0),Font=Art.Font(8.5f),ForeColor=Art.Muted,Text=preview?"演示画面；所有数值均为示例。":"关闭此窗口会收起网络中心；测速继续运行，可用「停止」取消。退出主程序会停止测速。"};
            Controls.Add(pages);Controls.Add(header);Controls.Add(footer);
            pages.SelectedIndexChanged+=delegate{traffic.RefreshData();};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(!shutdown){e.Cancel=true;Hide();}};
            if(preview){speed.LoadDemo();traffic.LoadDemo();tools.LoadDemo();}
        }
        private void AddPage(string name,Control content){TabPage page=new TabPage(name){BackColor=Art.Bg,Padding=new Padding(8)};page.Controls.Add(content);pages.TabPages.Add(page);}
        internal void ShowPage(int index)
        {
            pages.SelectedIndex=Math.Max(0,Math.Min(2,index));
            if(index==3)tools.ShowInformation();else if(index==2)tools.ShowDiagnostics();
            if(!Visible)Show();if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;Activate();RefreshData();
        }
        internal void RefreshData(){if(!shutdown&&!IsDisposed)traffic.RefreshData();}
        internal void Shutdown(){if(shutdown)return;shutdown=true;speed.Cancel();speed.Shutdown();tools.Shutdown();Close();}
        protected override void Dispose(bool disposing){if(disposing&&!shutdown){shutdown=true;speed.Cancel();speed.Shutdown();tools.Shutdown();}base.Dispose(disposing);}
    }
}
