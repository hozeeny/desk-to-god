using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
namespace NetBunny
{
    internal static class DesktopSourceTests
    {
        static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        static int checks;
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
        [STAThread] static int Main(string[] args)
        {
            string profile=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"profiles","source-tests-"+Guid.NewGuid().ToString("N"));
            try
            {
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
                Console.WriteLine("Desktop sources: "+checks+" checks passed; synthetic census only, no system worker started.");return 0;
            }
            catch(Exception e){Console.Error.WriteLine(e.GetBaseException().Message);return 1;}
        }
    }
}
