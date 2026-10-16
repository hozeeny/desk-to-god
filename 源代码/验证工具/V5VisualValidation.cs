using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NetBunny;

internal static class V5VisualValidation
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
            using(Form missingOwner=new Form())
            {
                bool missingReported=false;
                try { using(AdventureController missing=new AdventureController(missingOwner,Path.Combine(outDir,"intentionally-missing-assets"),true)){} }
                catch(InvalidDataException ex) { missingReported=ex.Message.Contains("角色素材不完整"); }
                Check(missingReported,"missing assets fail explicitly before network services or desktop windows start");
            }
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
                effects.FireField(new PointF(-22000,-22000),Enumerable.Range(0,20).Select(i=>new PointF(-21800+i*10,-21900)),1);
                Check(effects.ActiveCount==13,"mass clear effects capped at thirteen visible monsters");
                Check(Application.OpenForms.Cast<Form>().OfType<KnightLayeredWindow>().Where(w=>w.Text.Contains("武器特效")).All(w=>(GetWindowLong(w.Handle,-20)&0x20)!=0),"all mass clear effects pass mouse input through");
                effects.Clear();Check(effects.ActiveCount==0,"cancel disposes all mass clear effect windows");
                effects.FireField(new PointF(-22000,-22000),Enumerable.Range(0,5).Select(i=>new PointF(-21800+i*10,-21900)),1);
                for(int i=0;i<60;i++)effects.Tick(.02);
                Check(effects.ActiveCount==0,"mass clear effect lifetime completes");
                GC.Collect();GC.WaitForPendingFinalizers();int before=GetGuiResources(Process.GetCurrentProcess().Handle,0);
                for(int j=0;j<30;j++){effects.Fire(new PointF(-22000,-22000),new PointF(-21800,-21900),weapons[j%weapons.Length],1);for(int i=0;i<60;i++)effects.Tick(.02);}
                GC.Collect();GC.WaitForPendingFinalizers();int after=GetGuiResources(Process.GetCurrentProcess().Handle,0);Check(after<=before+5,"effects GDI bounded "+before+" -> "+after);
            }
            KnightBrand.ExportIcon(Path.Combine(Path.GetDirectoryName(assets),"knight.ico"));
            using(Bitmap logo=new Bitmap(256,256)) { using(Graphics g=Graphics.FromImage(logo))KnightBrand.DrawHelmet(g,new Rectangle(0,0,256,256));logo.Save(Path.Combine(outDir,"新版头盔图标.png")); }
            foreach(int width in new[]{1000,1224})
            using(MainForm main=new MainForm(true)) {
                main.StartPosition=FormStartPosition.Manual;main.Location=new Point(-24000,-24000);main.Width=width;main.Show();Application.DoEvents();
                Control hero=main.Controls.Cast<Control>().First(p=>p.Controls.Cast<Control>().Any(c=>c.Text=="一键清场"));
                Control[] buttons=hero.Controls.Cast<Control>().Where(c=>c is SoftButton).OrderBy(c=>c.Left).ToArray();
                Check(buttons.Length==7,"main seven adventure controls width="+width);
                Check(buttons.All(c=>c.Left>=0&&c.Right<=hero.ClientSize.Width&&c.Bottom<=hero.ClientSize.Height),"main adventure controls fit width="+width);
                Check(Enumerable.Range(1,buttons.Length-1).All(i=>!buttons[i-1].Bounds.IntersectsWith(buttons[i].Bounds)),"main adventure controls no overlap width="+width);
                using(Bitmap bitmap=new Bitmap(main.Width,main.Height)){main.DrawToBitmap(bitmap,new Rectangle(Point.Empty,main.Size));bitmap.Save(Path.Combine(outDir,"主界面-"+width+".png"));}
                main.Close();
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
            Console.WriteLine("V5 visuals: "+count+" checks passed; isolated offscreen windows, no cleanup or network changes.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
