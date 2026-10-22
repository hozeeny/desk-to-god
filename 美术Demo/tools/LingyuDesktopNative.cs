using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetBunny
{
    // Explicit, serial, two-process native fixture. All combat and buttons use
    // the existing production controller; the qishi-full initial seed is declared.
    internal sealed class LingyuDesktopNative
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        readonly DesktopDemoSession session;
        readonly DesktopDemoAcceptance native;
        readonly Timer timer = new Timer { Interval = 50 };
        readonly Stopwatch wall = Stopwatch.StartNew();
        readonly Dictionary<string, object> report = new Dictionary<string, object>();
        readonly Dictionary<string, string> realBefore;
        readonly string output, marker;
        readonly bool restore;
        readonly bool crowdProbe;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        Dictionary<string, object> expected;
        bool busy, finished;
        int phase, steps;
        double next, startedBattle;
        string mirrorId;
        string pausedBeforeNavigation;
        double returnTaskOpenedAt;
        long qiBefore;
        double crowdStarted, nextCrowdFrame;
        string[] crowdIds;
        int crowdFrame;
        internal int ExitCode { get; private set; }
        AdventureController Game { get { return session.Game; } }
        object Call(object instance, string method, params object[] args) { return instance.GetType().GetMethod(method, Hidden).Invoke(instance, args); }
        object Native(string method, params object[] args) { return Call(native, method, args); }
        T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, Hidden).GetValue(instance); }
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); ((List<string>)report["checks"]).Add(text); }
        bool Click(Control control) { return (bool)Native("ClickControl", control); }
        LingyuForm Panel { get { return Application.OpenForms.OfType<LingyuForm>().Single(f => f.Visible); } }
        [STAThread] static int Main(string[] args)
        {
            try
            {
                string profile = Path.GetFullPath(args[3]), output = Path.GetFullPath(args[2]);
                string allowed = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles") + Path.DirectorySeparatorChar;
                bool restore = args[4] == "restore";
                if ((args[4] != "prepare" && !restore) || !profile.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)
                    || !Path.GetFileName(profile).StartsWith("verify-", StringComparison.Ordinal) || Directory.Exists(output)
                    || (!restore && Directory.Exists(profile))) throw new IOException("Fresh private native fixture/output required.");
                Directory.CreateDirectory(output); SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[1]));
                using (DesktopDemoSession session = new DesktopDemoSession(Path.GetFullPath(args[0]), profile, true, restore ? null : "qishi-full"))
                {
                    LingyuDesktopNative test = new LingyuDesktopNative(session, output, restore, args.Length > 5 && args[5] == "crowd"); Application.Run(session); return test.ExitCode;
                }
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
        LingyuDesktopNative(DesktopDemoSession session, string output, bool restore, bool crowdProbe)
        {
            this.session = session; this.output = output; this.restore = restore; ExitCode = 1;
            this.crowdProbe = crowdProbe && restore;
            marker = Path.Combine(session.ProfileRoot, "lingyu-suspend-native-marker.json"); native = new DesktopDemoAcceptance(session, output, true);
            Native("SnapshotRealArchives");
            realBefore = new Dictionary<string, string>(Field<Dictionary<string, string>>(native, "realArchives"));
            report["phase"] = restore ? "restore" : "prepare"; report["processId"] = Process.GetCurrentProcess().Id;
            report["scope"] = "normal x1 private qishi-full initial feature fixture; native buttons and actual mirror click; no HP/time/probability injection; runner compiled with production source, not exact delivered EXE";
            report["checks"] = new List<string>(); timer.Tick += Tick; timer.Start(); next = .6;
        }
        PointF Feet(AdventureEnemy enemy)
        {
            Size size=(Size)Call(Game,"EnemySize",enemy);
            return new PointF((float)(enemy.X+size.Width/2.0),(float)(enemy.Y+size.Height));
        }
        void BeginCrowdProbe(double now)
        {
            AdventureEnemy[] group=Game.Battle.Enemies.Where(a=>!a.IsBoss&&!a.Defeated&&!a.VisualDefeated&&a.IsVirtual).ToArray();
            Check(group.Length==3,"crowd fixture has exactly two existing ordinary creatures plus the formal mirror");
            Rectangle work=Field<Rectangle>(Game,"area");double scale=Field<float>(Game,"displayScale");
            double feetX=work.Left+work.Width*.5,feetY=work.Top+Math.Max(240*scale,work.Height*.62);
            Game.Roaming=false;
            Game.GetType().GetField("x",Hidden).SetValue(Game,feetX-110*scale);
            Game.GetType().GetField("y",Hidden).SetValue(Game,feetY-240*scale);
            foreach(AdventureEnemy enemy in group)
            {
                Size size=(Size)Call(Game,"EnemySize",enemy);enemy.X=feetX-80*scale-size.Width/2.0;enemy.Y=feetY-size.Height;
            }
            crowdIds=group.Select(a=>a.Id).ToArray();crowdStarted=now;nextCrowdFrame=now;phase=107;
            report["crowdFixture"]="Only the positions of the existing three private virtual actors are deliberately stacked; roaming off. Production x1 timers, HP, attack rules, realms and rewards unchanged. Not natural positioning or the exact shipped EXE.";
            report["crowdFrames"]=new List<object>();Call(Game,"RenderWorld",true);
        }
        void CrowdFrame(double now)
        {
            if(now<nextCrowdFrame || crowdFrame>=24)return;nextCrowdFrame=now+.4;
            Rectangle bounds=Game.DesktopEnemyWindows.Values.Where(w=>w.Visible).Select(w=>w.Bounds).Concat(new[]{Game.DesktopHeroWindow.Bounds}).Aggregate(Rectangle.Union);
            bounds.Inflate(24,24);bounds=Rectangle.Intersect(bounds,SystemInformation.VirtualScreen);
            string name="crowd-"+(crowdFrame++).ToString("D3")+".png";
            using(Bitmap frame=DesktopFrameCapture.CaptureRegion(bounds,true))frame.Save(Path.Combine(output,name));
            ((List<object>)report["crowdFrames"]).Add(new Dictionary<string,object>{{"image",name},{"seconds",now-crowdStarted},{"bounds",new[]{bounds.X,bounds.Y,bounds.Width,bounds.Height}}});
        }
        bool Painted(LingyuForm panel)
        {
            panel.Refresh();
            foreach (string field in new[] { "nodeTitle", "nodeText" })
            {
                Label label = Field<Label>(panel, field); label.Refresh(); int samples = 0, matches = 0;
                using (Bitmap expected = new Bitmap(label.Width, label.Height))
                // DrawToBitmap includes the border. PointToScreen(0,0) starts at
                // the client origin and shifts bordered labels by one pixel.
                using (Bitmap actual = DesktopFrameCapture.CaptureRegion(new Rectangle(label.Parent.PointToScreen(label.Location), label.Size), true))
                {
                    label.DrawToBitmap(expected, new Rectangle(Point.Empty, label.Size)); Color ink = label.ForeColor;
                    for (int y = 0; y < expected.Height; y++) for (int x = 0; x < expected.Width; x++)
                    {
                        Color source = expected.GetPixel(x, y); if (Math.Abs(source.R-ink.R)+Math.Abs(source.G-ink.G)+Math.Abs(source.B-ink.B)>25) continue;
                        Color pixel=actual.GetPixel(x,y); samples++; if(Math.Abs(source.R-pixel.R)+Math.Abs(source.G-pixel.G)+Math.Abs(source.B-pixel.B)<80)matches++;
                    }
                    if(samples>=60 && matches<samples*.8 && !report.ContainsKey(field+"Diagnostic"))
                    {
                        expected.Save(Path.Combine(output,field+"-draw-to-bitmap-diagnostic.png"));actual.Save(Path.Combine(output,field+"-native-diagnostic.png"));
                        report[field+"Diagnostic"]=label.Bounds.ToString()+"; client="+label.ClientRectangle+"; color="+label.ForeColor;
                    }
                }
                report[field+"Glyphs"] = matches+"/"+samples;
                if (samples < 60 || matches < samples*.8) return false;
            }
            return true;
        }
        string PausedState()
        {
            LingyuProgress p = Game.Lingyu;
            return json.Serialize(new { p.ExpeditionActive, p.ExpeditionNode, p.ExpeditionDepth, p.CombatPending, p.CombatOutcome, p.PendingJiyuanId, p.EventCounter,
                flags = p.ExpeditionFlags.OrderBy(s => s).ToArray(), grants = p.Grants.OrderBy(v => v.Key).ToArray(), qi = p.PendingQi.OrderBy(v => v.Key).ToArray(),
                materials = p.Materials.OrderBy(v => v.Key).ToArray(), visits = p.RealmVisits.OrderBy(v => v.Key).ToArray(), scrolls = p.Scrolls.OrderBy(v => v.Key).ToArray(), heritages = p.Heritages.ToArray(),
                choices = p.JiyuanChoices.OrderBy(v => v.Key).ToArray(), entered = p.RealmEntered.OrderBy(s => s).ToArray(), log = p.ExpeditionLog.ToArray() });
        }
        void Tick(object sender, EventArgs e)
        {
            if (busy || finished) return; busy=true;
            try
            {
                report["lastStep"]=phase;
                double now=wall.Elapsed.TotalSeconds;
                if(now>100)throw new TimeoutException("Native suspension flow exceeded 100 seconds; evidence retained.");
                if(now<next)return;
                if (!Game.DesktopAcceptanceMaintenanceOff()) throw new InvalidOperationException("Maintenance authorization changed during native fixture.");
                if(phase==0)
                {
                    if(restore)
                    {
                        expected=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(marker));
                        Check(Convert.ToInt32(expected["processId"])!=Process.GetCurrentProcess().Id,"a genuinely new process restores the paused profile");
                        Check(Game.Cultivation.DaoTitle==Convert.ToString(expected["title"]) && !Game.Lingyu.ExpeditionActive && Game.Lingyu.CombatPending
                            && Game.Lingyu.CombatOutcome==Convert.ToString(expected["outcome"]) && Game.Lingyu.ExpeditionNode==Convert.ToString(expected["node"])
                            && Game.Lingyu.Grants.Count==Convert.ToInt32(expected["grants"]),"paused identity, node and unresolved outcome restore without rewards or automatic re-entry");
                        Check(!Game.Battle.Enemies.Any(a=>a.NoFieldClear),"paused startup never spawns its mirror");
                        pausedBeforeNavigation=PausedState();
                        session.OpenPanel("practice"); returnTaskOpenedAt=wall.Elapsed.TotalSeconds; phase=60; next=returnTaskOpenedAt+.5;
                    }
                    else { session.OpenPanel("practice"); phase=1; next=now+.2; }
                }
                else if(phase==1)
                {
                    DesktopPracticePanel handbook=Application.OpenForms.OfType<DesktopPracticePanel>().Single(a=>a.Visible);
                    Button auto=Field<Button>(handbook,"automatic");
                    if(Game.AutoPlanning) { if(!Click(auto))return; }
                    phase=11;next=now+.15;
                }
                else if(phase==11)
                {
                    Check(!Game.AutoPlanning,"native handbook action disables automatic combat without granting maintenance");
                    session.ClosePanels();session.OpenPanel("lingyu");phase=2;next=now+.2;
                }
                else if(phase==2)
                {
                    if(!Painted(Panel)){if(now>12)throw new InvalidOperationException("Exploration labels never visibly presented: "+json.Serialize(report));return;}
                    if(!Game.Lingyu.ExpeditionActive){if(Click(Field<Button>(Panel,"continueExpedition")))next=now+.15;return;}
                    if(Game.Lingyu.CombatPending)
                    {
                        mirrorId=Game.Battle.Enemies.Single(a=>a.NoFieldClear&&!a.Defeated).Id;
                        session.ClosePanels();phase=3;next=now+.15;return;
                    }
                    string[] options=Game.Mirage.CurrentChoices();int selected=0;
                    if(options.Length==0)throw new InvalidOperationException("Active exploration has neither choices nor an unresolved battle.");
                    for(int i=0;i<options.Length;i++)if(options[i].Contains("击碎")||options[i].Contains("直面")){selected=i;break;}
                    Button choice=(Button)Native("FindButtonExact",options[selected]);if(Click(choice)){next=now+.15;if(++steps>45)throw new InvalidOperationException("No naturally generated mirror within 45 choices.");}
                }
                else if(phase==3)
                {
                    Native("Capture","01-mirror-before-suspension",true,false,true);
                    session.OpenPanel("lingyu");phase=4;next=now+.15;
                }
                else if(phase==4)
                {
                    if(!Painted(Panel))return;
                    Check(Field<Button>(Panel,"suspendExpedition").Enabled,"suspend is available in the current pending battle");
                    Native("CapturePanel",Panel,"02-active-expedition-panel");
                    if(!Click(Field<Button>(Panel,"suspendExpedition")))return;
                    phase=14;next=now+.15;
                }
                else if(phase==14)
                {
                    Check(!Game.Lingyu.ExpeditionActive && Game.Lingyu.CombatPending && !Game.Battle.Enemies.Any(a=>a.Id==mirrorId),"native suspend removes only the mirror and retains its unresolved outcome");
                    phase=5;next=now+.15;
                }
                else if(phase==5)
                {
                    if(!Painted(Panel))return;Native("CapturePanel",Panel,"03-paused-expedition-panel");
                    Check(Game.DesktopSaveNow(),"all five private archives save the paused expedition");
                    File.WriteAllText(marker,json.Serialize(new Dictionary<string,object>{{"processId",Process.GetCurrentProcess().Id},{"title",Game.Cultivation.DaoTitle},{"node",Game.Lingyu.ExpeditionNode},{"outcome",Game.Lingyu.CombatOutcome},{"grants",Game.Lingyu.Grants.Count}}));
                    session.ClosePanels();qiBefore=Game.Cultivation.LifetimeQi;phase=10;next=now+1.5;
                }
                else if(phase==10)
                {
                    Check(Game.Cultivation.LifetimeQi>qiBefore || Game.Cultivation.RealmComplete,"leaving the expedition keeps desktop cultivation active");
                    Native("Capture","04-paused-desktop-cultivation",false,false,false);Finish(null);
                }
                else if(phase==6)
                {
                    if(!Painted(Panel))return;Native("CapturePanel",Panel,"05-restored-paused-panel");
                    if(!Click(Field<Button>(Panel,"continueExpedition")))return;
                    phase=16;next=now+.15;
                }
                else if(phase==60)
                {
                    DesktopPracticePanel handbook=Application.OpenForms.OfType<DesktopPracticePanel>().Single(f=>f.Visible);
                    Check(Field<TabControl>(handbook,"tabs").SelectedIndex==0,"restored handbook opens its existing desktop-adventure page");
                    Button button=Field<Button[]>(handbook,"returnTaskButtons").Single(b=>(string)b.Tag=="lingyu");
                    Label text=Field<Label[]>(handbook,"returnTaskLabels").Single(l=>l.Name=="return-task-text-lingyu");
                    Check(button.Visible&&button.Enabled&&text.Visible&&text.Text.Contains("暂存")&&text.Text.Contains("待战")
                        &&button.Text=="查看灵域","the real restored pending battle produces its readable saved-task summary and short navigation button");
                    if(!(bool)Native("NativePreviewContentMatches",text,true)||!(bool)Native("NativePreviewContentMatches",button,true))
                    {if(wall.Elapsed.TotalSeconds-returnTaskOpenedAt>10)throw new InvalidOperationException("Return-task native text/button glyphs never reached the existing eighty-percent presentation threshold.");return;}
                    Check(PausedState()==pausedBeforeNavigation&&!Game.Battle.Enemies.Any(a=>a.NoFieldClear),"reading and presenting the restored return task neither re-enters nor settles the paused expedition");
                    report["returnTaskText"]=text.Text;report["returnTaskButton"]=button.Text;
                    report["returnTaskNativeReadable"]=true;report["returnTaskPresentation"]="Existing native foreground-glyph match >=80% for both summary Label and short Button; no remaining-visible-pixel denominator.";
                    report["returnTaskBeforeNavigation"]=pausedBeforeNavigation;
                    Native("CapturePanel",handbook,"05-restored-handbook-return-task");
                    if(!Click(button))return;
                    phase=61;next=wall.Elapsed.TotalSeconds+.2;
                }
                else if(phase==61)
                {
                    LingyuForm panel=Application.OpenForms.OfType<LingyuForm>().SingleOrDefault(f=>f.Visible);
                    if(panel==null){if(wall.Elapsed.TotalSeconds-returnTaskOpenedAt>12)throw new InvalidOperationException("The native return-task click did not open the existing Lingyu window.");return;}
                    Check(PausedState()==pausedBeforeNavigation&&!Game.Lingyu.ExpeditionActive&&Game.Lingyu.CombatPending
                        &&!Game.Battle.Enemies.Any(a=>a.NoFieldClear),"native return-task navigation only opens Lingyu: original node, pending outcome, choices, grants and inventory remain unchanged with no mirror spawned");
                    Check(Field<Button>(panel,"continueExpedition").Visible&&Field<Button>(panel,"continueExpedition").Enabled,
                        "the existing explicit Continue action is still required after return-task navigation");
                    report["returnTaskAfterNavigation"]=PausedState();report["returnTaskNavigationOnly"]=true;
                    phase=6;next=wall.Elapsed.TotalSeconds+.15;
                }
                else if(phase==16)
                {
                    Check(Game.Lingyu.ExpeditionActive&&Game.Lingyu.CombatPending&&Game.Battle.Enemies.Count(a=>a.NoFieldClear&&!a.Defeated)==1,"native continue restores exactly one mirror for the original pending battle");
                    mirrorId=Game.Battle.Enemies.Single(a=>a.NoFieldClear&&!a.Defeated).Id;
                    session.ClosePanels();phase=7;next=now+.15;
                }
                else if(phase==7)
                {
                    if(crowdProbe && crowdIds==null){BeginCrowdProbe(now);return;}
                    Native("Capture","06-restored-mirror-on-desktop",true,false,true);
                    AdventureEnemy mirror=Game.Battle.Enemies.Single(a=>a.Id==mirrorId);
                    if(!(bool)Native("ClickEnemy",mirror))return;
                    phase=17;next=now+.15;
                }
                else if(phase==107)
                {
                    CrowdFrame(now);
                    if(now-crowdStarted<8)return;
                    AdventureEnemy[] group=crowdIds.Select(id=>Game.Battle.Enemies.Single(a=>a.Id==id)).ToArray();
                    double scale=Field<float>(Game,"displayScale"), minimum=Double.MaxValue;
                    for(int i=0;i<group.Length;i++)for(int j=i+1;j<group.Length;j++)minimum=Math.Min(minimum,AdventureBattle.Distance(Feet(group[i]).X,Feet(group[i]).Y,Feet(group[j]).X,Feet(group[j]).Y));
                    report["crowdMinimumFeetDistance"]=minimum;
                    Check(minimum>=60*scale,"three previously stacked native actors separate by at least 60 scaled feet pixels through normal movement");
                    phase=7;next=now+.1;
                }
                else if(phase==17)
                {
                    Check(Game.Battle.Target!=null&&Game.Battle.Target.Id==mirrorId,"actual native click selects the specified mirror rather than an overlapping ordinary creature");
                    session.OpenPanel("practice");phase=8;next=now+.15;
                }
                else if(phase==8)
                {
                    DesktopPracticePanel handbook=Application.OpenForms.OfType<DesktopPracticePanel>().Single(a=>a.Visible);
                    if(!Game.AutoPlanning&&!Click(Field<Button>(handbook,"automatic")))return;
                    phase=18;next=now+.15;
                }
                else if(phase==18)
                {
                    Check(Game.AutoPlanning,"native handbook action restores the player's automatic combat choice");
                    session.ClosePanels();phase=9;startedBattle=now;next=now+.15;
                }
                else if(phase==9)
                {
                    if(Game.Lingyu.CombatPending){if(now-startedBattle>55)throw new TimeoutException("Restored mirror combat did not settle.");return;}
                    Check(!Game.Battle.Progress.Dead&&Game.Lingyu.Grants.Count>Convert.ToInt32(expected["grants"]),"restored mirror wins through actual combat and settles its formal reward");
                    Check(Game.DesktopSaveNow(),"restored exploration saves all five private archives");
                    LingyuProgress saved=LingyuProgress.Load(Path.Combine(session.ProfileRoot,"lingyu-v1.txt"));
                    Check(!saved.CombatPending&&saved.Grants.Count==Game.Lingyu.Grants.Count,"production Load retains the one settled result");
                    Native("Capture","07-resumed-combat-settled",false,false,false);Finish(null);
                }
            }
            catch(Exception error){Finish((error is TargetInvocationException&&error.InnerException!=null?error.InnerException:error).ToString());}
            finally{busy=false;}
        }
        void Finish(string error)
        {
            if(finished)return;finished=true;timer.Stop();timer.Dispose();
            try
            {
                if(error!=null && Application.OpenForms.OfType<LingyuForm>().Any(f=>f.Visible)) Native("CapturePanel",Panel,"00-failure-panel");
                string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KnightAssistant");
                Dictionary<string,string> after=realBefore.Keys.ToDictionary(name=>name,name=>Hash(Path.Combine(root,name)));
                bool unchanged=realBefore.All(pair=>pair.Value==after[pair.Key]);
                report["realArchivesBefore"]=realBefore;report["realArchivesAfter"]=after;report["realArchivesUnchanged"]=unchanged;
                if(!unchanged)error=(error==null?"":error+"\n")+"Real archive hash changed during the native fixture.";
                Check(Game.DesktopAcceptanceMaintenanceOff(),"maintenance authorization remained off");
                report["nativeClicks"]=Field<int>(native,"clicks");report["captures"]=Field<List<object>>(native,"captures");
                report["saveSucceeded"]=Game.DesktopSaveNow();
                if(!(bool)report["saveSucceeded"])error=(error==null?"":error+"\n")+"Private save failed.";
            }
            catch(Exception diagnostic){error=(error==null?"":error+"\n")+"Final diagnostics failed: "+diagnostic;}
            report["passed"]=error==null;report["error"]=error;report["elapsedSeconds"]=wall.Elapsed.TotalSeconds;ExitCode=error==null?0:1;
            try
            {
                File.WriteAllText(Path.Combine(output,"lingyu-native.json"),json.Serialize(report));
                Console.WriteLine("Lingyu native "+report["phase"]+" passed="+report["passed"]+"; seconds="+wall.Elapsed.TotalSeconds.ToString("F2"));
            }
            catch(Exception writeError){ExitCode=1;Console.Error.WriteLine(writeError);}
            finally{session.ExitAfterAcceptance(ExitCode);}
        }
        static string Hash(string path){if(!File.Exists(path))return "missing";using(var hash=System.Security.Cryptography.SHA256.Create())using(var input=File.OpenRead(path))return BitConverter.ToString(hash.ComputeHash(input)).Replace("-","");}
    }
}
