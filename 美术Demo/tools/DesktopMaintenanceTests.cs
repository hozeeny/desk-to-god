using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace NetBunny
{
    internal static class DesktopMaintenanceTests
    {
        static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static int checks;
        static T Get<T>(object o,string n){return (T)o.GetType().GetField(n,F).GetValue(o);}
        static void Set(object o,string n,object v){o.GetType().GetField(n,F).SetValue(o,v);}
        static object Call(object o,string n,params object[] a){return o.GetType().GetMethod(n,F).Invoke(o,a);}
        static void Check(bool okay,string text){if(!okay)throw new Exception("FAIL "+text);checks++;Console.WriteLine("PASS "+text);}
        static AdventureEnemy FileEnemy(string id)
        {return new AdventureEnemy{Id=id,Health=0,MaxHealth=1,Authorized=true,Cleaning=true,FellInCombat=true,VisualDefeated=true,Batch=new CleanupBatch(new List<CleanupCandidate>{new CleanupCandidate{Path=@"C:\KnightAssistantSynthetic\held.tmp",Length=2048,EligibleForCleanup=true}},1)};}
        static AdventureController Game(Form owner,string assets,string profile,bool observe)
        {var c=new AdventureController(owner,assets,false,null,false,profile,observe);Get<System.Windows.Forms.Timer>(c,"timer").Stop();c.DesktopSetActorsVisible(false);return c;}
        [STAThread] static int Main(string[] args)
        {
            string root=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"profiles","maintenance-"+Guid.NewGuid().ToString("N"));
            try
            {
                using(Form owner=new Form())
                {
                    IntPtr handle=owner.Handle;
                    using(var c=Game(owner,args[0],Path.Combine(root,"virtual"),false))
                    {
                        var add=typeof(AdventureController).GetMethod("DesktopAddProcessTarget",F);
                        Check(add!=null,"desktop provides explicit process-to-monster binding");
                        var identity=new ProcessIdentity{Id=Int32.MaxValue-1,StartedUtcTicks=DateTime.UtcNow.Ticks,ExecutablePath=@"C:\KnightAssistantSynthetic\owned-fixture.exe",Name="owned fixture"};
                        Check(!(bool)add.Invoke(c,new object[]{identity}),"virtual entry refuses process target binding");
                        Get<MaintenanceConsentStore>(c,"consent").CleanupEnabled=true;
                        var e=FileEnemy("virtual-ceiling");c.Battle.Enemies.Add(e);Call(c,"BeginCleanup",e);
                        Check(Get<Queue<AdventureEnemy>>(c,"pendingCleanup").Count==0&&Get<Task<CleanupResult>>(c,"cleanupTask")==null,"virtual entry refuses workers even if a scope flag is supplied");
                    }
                    using(var c=Game(owner,args[0],Path.Combine(root,"observed"),true))
                    {
                        Check(c.SetCleanupConsent(true),"file consent persists only in private profile");
                        var automatic=FileEnemy("automatic-without-maintenance");automatic.Authorization=AdventureAuthorization.Automatic;c.Battle.Enemies.Add(automatic);Call(c,"BeginCleanup",automatic);
                        Check(Get<Queue<AdventureEnemy>>(c,"pendingCleanup").Count==0,"automatic kill cannot execute attended-only file grant");
                        Set(c,"enabled",true);
                        Set(c,"simulatedCleaner",new Func<CleanupBatch,CancellationToken,Task<CleanupResult>>((b,t)=>Task.FromResult(new CleanupResult{DeletedFiles=2,FreedBytes=2048,SkippedFiles=1,Message="fixture partial"})));
                        int kills=c.Cultivation.MortalKills;
                        var e=FileEnemy("actual-accounting");e.Authorization=AdventureAuthorization.Manual;c.Battle.Enemies.Add(e);Call(c,"BeginCleanup",e);
                        Check(c.Cultivation.MortalKills==kills+1,"actual maintenance kill advances mortal growth independently of worker outcome");
                        Call(c,"BeginCleanup",e);Check(c.Cultivation.MortalKills==kills+1,"pending worker cannot grant the same cultivation kill twice");
                        Call(c,"DrainCleanup");
                        Check(c.Battle.Progress.FreedBytes==2048,"disk accounting uses the worker result only");
                        Array history=Get<Array>(c.ReadDesktopPlayerView(),"MaintenanceResults");
                        Check(history.Length>0&&Get<long>(history.GetValue(0),"FreedBytes")==2048&&Get<int>(history.GetValue(0),"DeletedFiles")==2,"desktop history exposes actual deleted bytes and files");
                        long before=c.Battle.Progress.FreedBytes;
                        Check(c.SetMemoryConsent(true),"memory has its own private consent");
                        Set(c,"simulatedMemoryReclaimer",new Func<CancellationToken,Task<MemoryReclaimResult>>(t=>Task.FromResult(new MemoryReclaimResult{Before=new MemorySnapshot{Available=true,UsedPercent=80},After=new MemorySnapshot{Available=true,UsedPercent=81},Trimmed=0,WorkingSetReduction=0,Error="fixture no reclaim"})));
                        var boss=new AdventureEnemy{Id="memory-observation",IsBoss=true,Authorized=true,Health=0,MaxHealth=1,Authorization=AdventureAuthorization.Manual};c.Battle.Enemies.Add(boss);Call(c,"BeginCleanup",boss);Call(c,"DrainMemoryReclaim");
                        history=Get<Array>(c.ReadDesktopPlayerView(),"MaintenanceResults");
                        Check(Get<string>(history.GetValue(0),"Summary").Contains("81.0%"),"observed memory rise is reported honestly");
                        Check(c.Battle.Progress.FreedBytes==before,"memory maintenance never creates disk bytes");
                        Check(c.SetCleanupConsent(false)&&c.SetMemoryConsent(false),"both grants revoke without stopping cultivation");
                        Check(!c.MaintenanceConsentCleanup&&!c.MaintenanceConsentMemory,"revoked scopes remain disabled");
                        var identity=new ProcessIdentity{Id=Int32.MaxValue-1,StartedUtcTicks=DateTime.UtcNow.Ticks,ExecutablePath=@"C:\KnightAssistantSynthetic\owned-fixture.exe",Name="owned fixture"};
                        Check((bool)Call(c,"DesktopAddProcessTarget",identity),"observed program requires explicit target binding");
                        Check(!(bool)Call(c,"DesktopAddProcessTarget",identity),"same process identity cannot produce duplicate targets");
                        var program=c.Battle.Enemies.Last();
                        Check((bool)Call(c,"DesktopMortalUnengaged",program),"unselected program target cannot attack newcomer");
                        Check(!(bool)Call(c,"AutoMaintenanceEligibility",program),"automatic planning never selects process-exit targets");
                        int confirmations=0;Set(c,"DesktopProcessConfirmation",new Func<ProcessIdentity,bool>(p=>{confirmations++;return false;}));
                        program.Authorized=true;program.Authorization=AdventureAuthorization.Manual;program.Health=0;program.FellInCombat=true;
                        Call(c,"BeginCleanup",program);
                        Check(confirmations==1&&Get<Task<ProcessCloseResult>>(c,"desktopProcessTask")==null,"declined one-shot confirmation starts no process action");
                        var unfinished=new TaskCompletionSource<ProcessCloseResult>();
                        Set(c,"desktopProcessTask",unfinished.Task);Set(c,"desktopProcessCancellation",new CancellationTokenSource());Set(c,"desktopProcessName","owned fixture");
                        c.Dispose();unfinished.SetResult(new ProcessCloseResult{Status=ProcessCloseStatus.Cancelled});
                        string historyFile=Path.Combine(root,"observed","maintenance-results-v1.log");
                        Check(File.ReadAllLines(historyFile).Any(line=>Encoding.UTF8.GetString(Convert.FromBase64String(line.Split('|').Last())).Contains("未核实")),"closing the game records an unfinished process outcome without claiming success");
                    }
                    string fixture=Path.Combine(root,"owned-files");Directory.CreateDirectory(fixture);
                    string deleted=Path.Combine(fixture,"old-owned.tmp"),held=Path.Combine(fixture,"held-owned.tmp");
                    foreach(string path in new[]{deleted,held}){File.WriteAllBytes(path,new byte[path==deleted?4096:512]);File.SetCreationTimeUtc(path,DateTime.UtcNow.AddDays(-2));File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-2));}
                    var service=(CleanupService)Activator.CreateInstance(typeof(CleanupService),F,null,new object[]{fixture,null},null);
                    CleanupSnapshot census=service.Scan(CancellationToken.None);
                    using(var c=Game(owner,args[0],Path.Combine(root,"owned-maintenance"),true))
                    using(var locked=new FileStream(held,FileMode.Open,FileAccess.Read,FileShare.None))
                    {
                        Set(c,"enabled",true);Check(c.SetCleanupConsent(true),"owned fixture uses a private explicit file grant");
                        Set(c,"simulatedCleaner",new Func<CleanupBatch,CancellationToken,Task<CleanupResult>>((batch,token)=>Task.FromResult(service.Clean(batch,token))));
                        var e=FileEnemy("owned-kernel-clean");e.Batch=census.Batches.Single();e.Authorization=AdventureAuthorization.Manual;c.Battle.Enemies.Add(e);
                        Call(c,"BeginCleanup",e);Call(c,"DrainCleanup");
                        Check(!File.Exists(deleted)&&File.Exists(held),"actual file worker deletes only the owned eligible file and skips the locked file");
                        Check(c.Battle.Progress.FreedBytes==4096,"actual deletion and displayed bytes agree exactly");
                    }
                    if(args.Length>2)
                    {
                        string marker=Path.Combine(root,"owned-program");
                        using(Process fixtureProcess=Process.Start(new ProcessStartInfo(args[2],"\""+marker+"\" close"){UseShellExecute=false}))
                        {
                            try
                            {
                                Stopwatch wait=Stopwatch.StartNew();while(!File.Exists(marker+".ready")&&wait.Elapsed.TotalSeconds<10)Thread.Sleep(20);
                                Check(File.Exists(marker+".ready"),"dedicated program fixture is ready");
                                ProcessIdentity identity=DesktopProcessTargets.Observe().FirstOrDefault(p=>p.Id==fixtureProcess.Id);
                                Check(identity!=null,"owned program has a verified closeable identity");
                                using(var c=Game(owner,args[0],Path.Combine(root,"owned-process"),true))
                                {
                                    Check((bool)Call(c,"DesktopAddProcessTarget",identity),"owned verified process binds to an actual game target");
                                    var enemy=c.Battle.Enemies.Single();enemy.Health=0;enemy.Authorized=true;enemy.Authorization=AdventureAuthorization.Manual;enemy.FellInCombat=true;
                                    Set(c,"DesktopProcessConfirmation",new Func<ProcessIdentity,bool>(p=>p.Id==fixtureProcess.Id));Call(c,"BeginCleanup",enemy);
                                    Task<ProcessCloseResult> action=Get<Task<ProcessCloseResult>>(c,"desktopProcessTask");
                                    Check(action!=null&&action.Wait(10000),"confirmed owned target completes its real normal-close request");
                                    Call(c,"DrainDesktopProcessAction");
                                    Check(fixtureProcess.WaitForExit(1000)&&Get<Array>(c.ReadDesktopPlayerView(),"MaintenanceResults").Length==1,"actual process exit produces exactly one recorded result");
                                }
                            }
                            finally{File.WriteAllText(marker+".stop","stop");fixtureProcess.WaitForExit(3000);}
                        }
                    }
                }
                Console.WriteLine("Desktop maintenance: "+checks+" checks passed; owned file/process fixtures and injected memory, no user targets.");return 0;
            }
            catch(Exception e){Console.Error.WriteLine(e.GetBaseException().Message);return 1;}
        }
    }
}
