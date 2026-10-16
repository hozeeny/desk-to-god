using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NetBunny;

internal static class V4VisualValidation
{
    [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr process,int flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLong")] static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wparam,IntPtr lparam);
    static int count;
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;}
    [STAThread] static int Main(string[] args)
    {
        try
        {
            SetProcessDPIAware();Application.EnableVisualStyles();string assets=args[0],outDir=args[1];Directory.CreateDirectory(outDir);
            using(KnightLayeredWindow actor=new KnightLayeredWindow())
            {
                actor.Location=new Point(-24000,-24000);int clicks=0,menus=0;actor.MouseClick+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left)clicks++;};actor.ContextRequested=delegate{menus++;};
                Bitmap sprite=new Bitmap(100,100);using(Graphics g=Graphics.FromImage(sprite))g.Clear(Color.Coral);actor.Present(sprite);actor.Show();
                IntPtr point=new IntPtr((40<<16)|40);SendMessage(actor.Handle,0x201,new IntPtr(1),point);SendMessage(actor.Handle,0x202,IntPtr.Zero,point);
                Check(clicks==1,"native left click reaches monster authorization handler");
                SendMessage(actor.Handle,0x204,new IntPtr(2),point);SendMessage(actor.Handle,0x205,IntPtr.Zero,point);Check(menus==1,"native right click reaches equipment menu");
            }
            using(AdventureEffects effects=new AdventureEffects())
            {
                string[] weapons={"spear","greatsword","wave","laser","boomerang","blackhole","automaton"};
                foreach(string weapon in weapons)
                {
                    effects.Fire(new PointF(-22000,-22000),new PointF(-21700,-21850),weapon,1);
                    Check(effects.ActiveCount==1,"projectile created: "+weapon);
                    KnightLayeredWindow layer=Application.OpenForms.Cast<Form>().OfType<KnightLayeredWindow>().First(w=>w.Text.Contains("武器特效"));
                    int style=GetWindowLong(layer.Handle,-20);Check((style&0x80000)!=0&&(style&0x20)!=0,"mouse-through layered style: "+weapon);
                    for(int i=0;i<60;i++)effects.Tick(.02);
                    Check(effects.ActiveCount==0,"projectile disposed: "+weapon);
                }
                for(int i=0;i<8;i++)effects.Fire(new PointF(-22000,-22000),new PointF(-21800,-21900),"wave",1);
                Check(effects.ActiveCount==3,"effect window budget");effects.Clear();Check(effects.ActiveCount==0,"hide cancels effects");
                GC.Collect();GC.WaitForPendingFinalizers();int before=GetGuiResources(Process.GetCurrentProcess().Handle,0);
                for(int j=0;j<30;j++){effects.Fire(new PointF(-22000,-22000),new PointF(-21800,-21900),weapons[j%weapons.Length],1);for(int i=0;i<60;i++)effects.Tick(.02);}
                GC.Collect();GC.WaitForPendingFinalizers();int after=GetGuiResources(Process.GetCurrentProcess().Handle,0);Check(after<=before+5,"effects GDI bounded "+before+" -> "+after);
            }
            using(Form owner=new Form())
            {
                IntPtr handle=owner.Handle;
                using(AdventureController c=new AdventureController(owner,assets,true))
                {
                    c.Battle.Progress.Level=7;c.Battle.Progress.Experience=138;c.Battle.Progress.Health=148;c.Battle.Progress.Score=3260;
                    c.Battle.Progress.Inventory=AdventureWeapon.All.Select(w=>w.Id).ToList();c.Battle.Progress.WeaponId="laser";
                    c.Battle.LastReward="界面演示数据 · 所有武器预览，不修改实际等级或文件";
                    using(AdventureEquipmentForm panel=new AdventureEquipmentForm(c))
                    {
                        panel.StartPosition=FormStartPosition.Manual;panel.Location=new Point(-24000,-24000);panel.RefreshData();panel.Show();Application.DoEvents();
                        using(Bitmap bitmap=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(bitmap,new Rectangle(Point.Empty,panel.Size));bitmap.Save(Path.Combine(outDir,"装备栏预览.png"));}
                        FlowLayoutPanel content=panel.Controls.OfType<FlowLayoutPanel>().First();content.AutoScrollPosition=new Point(0,600);Application.DoEvents();
                        using(Bitmap bitmap=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(bitmap,new Rectangle(Point.Empty,panel.Size));bitmap.Save(Path.Combine(outDir,"地区设置预览.png"));}
                        Check(!c.IsScanning&&c.Battle.QueueCount==0,"equipment preview no cleanup");
                    }
                }
            }
            Console.WriteLine("V4 visuals: "+count+" checks passed; isolated offscreen windows, no cleanup or network changes.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
