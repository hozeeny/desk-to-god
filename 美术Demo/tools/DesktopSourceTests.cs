using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace NetBunny
{
    internal static class DesktopSourceTests
    {
        static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        static int checks;
        static int nameFailures;
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        static object Call(object o, string n, params object[] a) { return o.GetType().GetMethod(n,F).Invoke(o,a); }
        static T Get<T>(object o,string n) { return (T)o.GetType().GetField(n,F).GetValue(o); }
        static void Set(object o,string n,object value) { o.GetType().GetField(n,F).SetValue(o,value); }
        static void Check(bool okay,string text) { if(!okay)throw new Exception("FAIL "+text);checks++;Console.WriteLine("PASS "+text); }
        static AdventureController Create(Form owner,string assets,string profile)
        {
            ConstructorInfo seven=typeof(AdventureController).GetConstructors().FirstOrDefault(x=>x.GetParameters().Length==7);
            return seven==null?new AdventureController(owner,assets,false,null,false,profile):(AdventureController)seven.Invoke(new object[]{owner,assets,false,null,false,profile,true});
        }
        static CleanupSnapshot Snapshot(params CleanupCandidate[] files)
        { return new CleanupSnapshot { ObservedFilesAvailable=true,ObservedPathsAvailable=true,ObservedFiles=files.ToList(),ObservedPaths=new HashSet<string>(files.Select(f=>f.Path),StringComparer.OrdinalIgnoreCase),InventoryComplete=true }; }
        static void NameCheck(bool okay,string text)
        { checks++; if(!okay)nameFailures++; Console.WriteLine((okay?"PASS ":"FAIL ")+text); }
        static bool SamePixels(Bitmap left,Bitmap right)
        {
            if(left.Size!=right.Size)return false;
            for(int y=0;y<left.Height;y++)for(int x=0;x<left.Width;x++)if(left.GetPixel(x,y).ToArgb()!=right.GetPixel(x,y).ToArgb())return false;
            return true;
        }
        static void CheckBossFrame(AdventureController c,AdventureEnemy enemy,string expected,string output)
        {
            KnightLayeredWindow window=c.DesktopEnemyWindows[enemy.Id]; Bitmap frame=Get<Bitmap>(window,"current");
            AdventureRenderState state=new AdventureRenderState {Kind=String.IsNullOrEmpty(enemy.EvilTheme)?"Boss":"EvilBoss",HeroStage=c.ArtDemoHeroStage,
                WeaponId=enemy.BossWeapon??"spear",MonsterKind=enemy.MonsterKind,Size=(Size)Call(c,"EnemySize",enemy),ThemeCode=enemy.EvilTheme??Get<RegionThemeService>(c,"region").CurrentCode,
                Time=enemy.AnimationTime,Phase="idle",PhaseTime=0,DeathProgress=0,Label=expected,QueueNumber=0,FacingRight=enemy.FacingRight,MoveSpeed=0,
                HealthRatio=(double)enemy.Health/enemy.MaxHealth,HealthCurrent=enemy.Health,HealthMaximum=enemy.MaxHealth,Opacity=1};
            frame.Save(Path.Combine(output,enemy.Id+"-presented-frame.png"),ImageFormat.Png);
            using(Bitmap named=Get<AdventureRenderer>(c,"renderer").Render(state))
                NameCheck(SamePixels(named,frame),enemy.Id+" real RenderWorld/Present bitmap uses the source-accurate label: "+expected);
        }
        static void SourceNames(string assets,string output)
        {
            Directory.CreateDirectory(output); IntPtr foreground=GetForegroundWindow();
            using(Form owner=new Form {ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-25000,-25000)})
            using(AdventureController c=Create(owner,assets,Path.Combine(output,"private-name-profile-"+Guid.NewGuid().ToString("N"))))
            {
                Get<Timer>(c,"timer").Stop();
                Rectangle screens=Screen.AllScreens.Select(s=>s.Bounds).Aggregate(Rectangle.Union),area=new Rectangle(screens.Left-25000,screens.Top-25000,2500,1600);
                Set(c,"area",area);Set(c,"x",area.Left+40d);Set(c,"y",area.Top+40d);
                var file=new CleanupCandidate {Path=@"C:\KnightAssistantSynthetic\names-owned.tmp",Length=4096,Created=1,EligibleForCleanup=true};
                var batch=new CleanupBatch(new List<CleanupCandidate>{file},1);
                AdventureEnemy memory=EnemyBalance.MemoryBoss(c.Battle.Progress,c.Battle.Weapon,new Random(29));memory.Id="memory-name";
                AdventureEnemy disk=new AdventureEnemy {Id="disk-name",IsBoss=true,IsDiskBoss=true,MonsterKind="boss",Batch=batch,Health=100,MaxHealth=100};
                AdventureEnemy warning=new AdventureEnemy {Id="warning-name",IsBoss=true,IsDiskBoss=true,MonsterKind="boss",Health=100,MaxHealth=100};
                var cases=new[]{
                    new {Enemy=memory,Name="内存分身"},new {Enemy=disk,Name="磁盘危机"},new {Enemy=warning,Name="磁盘空间预警"},
                    new {Enemy=new AdventureEnemy {Id="process-name",ProcessTarget=new ProcessIdentity {Name="Owned Source App"},IsBoss=true,NoFieldClear=true,IsVirtual=true,Health=100,MaxHealth=100},Name="程灵 · Owned Source App"},
                    new {Enemy=new AdventureEnemy {Id="mirror-lord-name",NoFieldClear=true,IsBoss=true,IsDiskBoss=true,IsVirtual=true,Health=100,MaxHealth=100},Name="蜃楼主镜"},
                    new {Enemy=new AdventureEnemy {Id="mirror-name",NoFieldClear=true,IsVirtual=true,Health=100,MaxHealth=100},Name="蜃楼镜妖"},
                    new {Enemy=new AdventureEnemy {Id="virtual-name",IsVirtual=true,MonsterKind="slime",Health=100,MaxHealth=100},Name="苔露灵团"},
                    new {Enemy=new AdventureEnemy {Id="elite-name",IsVirtual=true,Elite=true,MonsterKind="mushroom",Health=100,MaxHealth=100},Name="精英 · 赤伞菇妖"},
                    new {Enemy=new AdventureEnemy {Id="virtual-boss-name",IsVirtual=true,IsBoss=true,MonsterKind="slime",Health=100,MaxHealth=100},Name="苔露灵团"}
                };
                for(int i=0;i<cases.Length;i++){AdventureEnemy e=cases[i].Enemy;e.X=area.Left+600+(i%3)*650;e.Y=area.Top+100+(i/3)*400;c.Battle.Enemies.Add(e);}
                // Bypass Enabled's census trigger only in this stopped fixture.
                // The real RenderWorld/Present still creates screen-external
                // windows; no physical screen, message pump or worker is used.
                Set(c,"enabled",true);Call(c,"RenderWorld",true);
                DesktopPlayerView view=c.ReadDesktopPlayerView();
                foreach(var item in cases)
                {
                    NameCheck(view.Enemies.Single(e=>e.Id==item.Enemy.Id).Name==item.Name,item.Enemy.Id+" player/source view identifies "+item.Name);
                    NameCheck(c.DesktopEnemyWindows[item.Enemy.Id].Text==item.Name,item.Enemy.Id+" real actor window title identifies "+item.Name);
                }
                foreach(var item in cases.Take(3))CheckBossFrame(c,item.Enemy,item.Name,output);
                c.Battle.SelectDesktopTarget(memory);view=c.ReadDesktopPlayerView();
                NameCheck(view.TargetName=="内存分身 "+memory.Health+"/"+memory.MaxHealth&&view.TargetSourceDetail.Contains("系统可用内存采样")&&view.TargetAction.Contains("未授权"),"selected memory source keeps its accurate name, existing HP suffix and never claims permission or cleanup");
                KnightLayeredWindow same=c.DesktopEnemyWindows[warning.Id];warning.Batch=batch;warning.VisualDirty=true;Call(c,"RenderWorld",true);
                NameCheck(Object.ReferenceEquals(same,c.DesktopEnemyWindows[warning.Id])&&same.Text=="磁盘危机"&&c.ReadDesktopPlayerView().Enemies.Single(e=>e.Id==warning.Id).Name=="磁盘危机","existing disk warning becomes a candidate-backed disk threat without a stale title or recreated window");
                warning.Batch=null;warning.VisualDirty=true;Call(c,"RenderWorld",true);
                NameCheck(Object.ReferenceEquals(same,c.DesktopEnemyWindows[warning.Id])&&same.Text=="磁盘空间预警"&&c.ReadDesktopPlayerView().Enemies.Single(e=>e.Id==warning.Id).Name=="磁盘空间预警","same disk threat returns to an honest no-file warning without a stale title");
                NameCheck(!owner.Visible&&!Get<Timer>(c,"timer").Enabled&&c.DesktopRealWorkIdle&&!c.MaintenanceConsentCleanup&&!c.MaintenanceConsentMemory&&!c.MaintenanceConsentAutoMaintenance
                    &&c.DesktopEnemyWindows.Values.Concat(new[]{c.DesktopHeroWindow}).All(window=>Screen.AllScreens.All(screen=>!screen.Bounds.IntersectsWith(window.Bounds))),"all naming/frame fixtures stay outside every physical screen, stopped, private and maintenance-free");
            }
            NameCheck(GetForegroundWindow()==foreground,"real off-screen source rendering never activates an application or changes foreground focus");
            if(nameFailures>0)throw new Exception("Source naming: "+nameFailures+" failed name/title/presented-frame assertions.");
        }
        [STAThread] static int Main(string[] args)
        {
            string profile=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"profiles","source-tests-"+Guid.NewGuid().ToString("N"));
            try
            {
                string art=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])),"ArtDemoAssets");
                if(Directory.Exists(art))Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable,art);
                using(Form owner=new Form())
                {
                    IntPtr handle=owner.Handle;
                    using(AdventureController c=Create(owner,args[0],profile))
                    {
                        Get<Timer>(c,"timer").Stop();
                        CleanupCandidate file=new CleanupCandidate {Path=@"C:\KnightAssistantSynthetic\source-old.tmp",Length=4096,Created=1,EligibleForCleanup=true};
                        Call(c,"ApplyInventory",Snapshot(file),false);
                        AdventureEnemy e=c.Battle.Enemies.Single();
                        DesktopPlayerView view=c.ReadDesktopPlayerView();
                        Check(view.Enemies.Any(x=>x.Id==e.Id),"real file target is visible in desktop player view");
                        Check(Get<string>(view.Enemies.Single(),"Source").Contains("临时文件"),"file target identifies its source");
                        Check(Get<string>(view.Enemies.Single(),"Action").Contains("未授权"),"source is not a system-maintenance grant");
                        Check((bool)Call(c,"DesktopMortalUnengaged",e),"unselected real ordinary target does not attack newcomer");
                        var pressure=new AdventureEnemy{Id="unselected-pressure",IsBoss=true,EvilTheme="CN",Health=100,MaxHealth=100};
                        Check((bool)Call(c,"DesktopMortalUnengaged",pressure),"memory pressure cannot ambush an unengaged new mortal");
                        Check(c.DesktopChooseEnemy(e.Id,false)&&c.Battle.Target==e,"real file target is selectable without maintenance");
                        Check(!c.MaintenanceConsentCleanup&&!c.MaintenanceConsentMemory,"selecting target never grants maintenance");
                        Call(c,"ApplyInventory",Snapshot(file),false);
                        Check(c.Battle.Enemies.Count==1,"repeated source census does not duplicate actors");
                        AdventureEnemy active=new AdventureEnemy {Id="active-file",Batch=new CleanupBatch(new List<CleanupCandidate>{file},2),Health=10,MaxHealth=10};
                        c.Battle.Enemies.Add(active);c.Battle.Authorize(active);active.Health=0;active.Cleaning=active.CleanupStarted=true;
                        AdventureEnemy other=new AdventureEnemy {Id="other",IsVirtual=true,Health=10,MaxHealth=10};c.Battle.Enemies.Add(other);
                        Check(c.DesktopChooseEnemy(other.Id,false)&&c.Battle.Target==other,"can switch target while preserving an unrelated worker");
                        Check(active.CleanupStarted&&active.Authorized,"selection preserves started maintenance ownership");
                        c.Battle.Enemies.Remove(active);
                        c.Battle.Enemies.Remove(other);
                        c.Battle.CancelQueue();
                        Call(c,"ApplyInventory",Snapshot(),false);
                        Check(!c.Battle.Enemies.Any(x=>x.Id==e.Id),"complete census retires vanished unselected source");
                        Set(c,"lastRefill",-100.0);
                        Call(c,"ApplyInventory",Snapshot(Enumerable.Range(0,400).Select(i=>new CleanupCandidate{Path=@"C:\KnightAssistantSynthetic\bulk-"+i,Length=1024,Created=1,EligibleForCleanup=true}).ToArray()),false);
                        Check(c.Battle.Enemies.Count<=3,"observed desktop field stays within three ordinary actors");
                    }
                }
                SourceNames(args[0],Path.Combine(args[1],"source-name-frames"));
                Console.WriteLine("Desktop sources: "+checks+" checks passed; synthetic census only, no system worker started.");return 0;
            }
            catch(Exception e){Console.Error.WriteLine(e.GetBaseException().Message);return 1;}
        }
    }
}
