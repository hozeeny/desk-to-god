using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NetBunny;

internal static class V54Validation
{
    static PointF Move(PointF p,PointF q,double speed,double dt)
    {
        double dx=q.X-p.X,dy=q.Y-p.Y,d=Math.Sqrt(dx*dx+dy*dy);
        if(d<.0001)return p; double step=Math.Min(d,speed*dt); return new PointF((float)(p.X+dx/d*step),(float)(p.Y+dy/d*step));
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Directory.CreateDirectory(args[1]); int checks=0;
            foreach(AdventureWeapon player in AdventureWeapon.All)
            {
                double sum=0,min=9999,max=0;
                foreach(AdventureWeapon foe in AdventureWeapon.All)
                {
                    CombatTactics plan=new CombatTactics(); PointF hero=new PointF(500,400),boss=new PointF(700,400);
                    double hp=EnemyBalance.MemoryBossHealth(1,player),cooldown=0,t=0; const double dt=.02;
                    for(;t<300&&hp>0;t+=dt)
                    {
                        cooldown=Math.Max(0,cooldown-dt);
                        CombatTacticsDecision choice=plan.Plan(new CombatTacticsInput {Automatic=true,TargetId="boss",Hero=hero,Target=boss,MovementBounds=new RectangleF(110,240,1060,600),Scale=1,WeaponRange=player.Range*.8,Cooldown=cooldown,HealthRatio=1,Time=t,Threats=new[]{boss}});
                        if(choice.Move)hero=Move(hero,choice.Destination,200,dt);
                        if(choice.Attack){hp-=player.Damage;cooldown=player.Cooldown;}
                        double d=AdventureBattle.Distance(hero.X,hero.Y,boss.X,boss.Y);
                        if(d>Math.Min(210,foe.Range*.5))boss=Move(boss,hero,56,dt);
                    }
                    if(hp>0)throw new Exception("Boss combat stalled: "+player.Id+"/"+foe.Id);
                    sum+=t;min=Math.Min(min,t);max=Math.Max(max,t);checks++;
                }
                if (sum/7 < 115 || sum/7 > 125) throw new Exception("Boss duration outside target: "+player.Id);
                Console.WriteLine(player.Id+": average="+(sum/7).ToString("0.0")+"s range="+min.ToString("0.0")+"–"+max.ToString("0.0")+"s HP="+EnemyBalance.MemoryBossHealth(1,player));
            }
            using(AdventureRenderer renderer=new AdventureRenderer(args[0]))
            using(Bitmap sheet=new Bitmap(6*240,2*270))
            using(Graphics g=Graphics.FromImage(sheet))
            using(Font f=new Font("Microsoft YaHei UI",12))
            {
                g.Clear(Color.FromArgb(27,25,39));
                for(int i=0;i<6;i++)for(int row=0;row<2;row++)
                {
                    AdventureRenderState state=new AdventureRenderState {Kind="EvilBoss",ThemeCode=RasterActorRenderer.ThemeCodes[i],WeaponId=AdventureWeapon.All[(i+row)%7].Id,Phase=row==0?"run":"attack",Time=.3,Cycle=.25,AttackProgress=row==0?0:.7,FacingRight=row==0,Size=new Size(220,240),HealthCurrent=2000,HealthMaximum=3200,Label="邪恶分身 · "+RasterActorRenderer.ThemeCodes[i]};
                    using(Bitmap image=renderer.Render(state)){g.DrawImage(image,i*240+10,row*270);checks++;}
                    g.DrawString(state.WeaponId,f,Brushes.White,i*240+20,row*270+244);
                    state.Phase="shatter";state.DeathProgress=1;
                    using(Bitmap gone=renderer.Render(state))
                    { for(int y=0;y<gone.Height;y+=4)for(int x=0;x<gone.Width;x+=4)if(gone.GetPixel(x,y).A!=0)throw new Exception("Evil boss death not transparent");checks++; }
                }
                sheet.Save(Path.Combine(args[1],"邪恶分身-六皮肤双向随机武器.png"),ImageFormat.Png);
            }
            using(Form owner=new Form())
            {
                IntPtr handle=owner.Handle;
                using(AdventureController c=new AdventureController(owner,args[0],true))
                using(AdventureEquipmentForm panel=new AdventureEquipmentForm(c))
                {
                    panel.Width=650;panel.StartPosition=FormStartPosition.Manual;panel.Location=new Point(-24000,-24000);panel.Show();panel.RefreshData();Application.DoEvents();
                    using(Bitmap frame=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(frame,new Rectangle(Point.Empty,frame.Size));frame.Save(Path.Combine(args[1],"装备栏-实时资源.png"));}
                    checks++;
                }
            }
            Console.WriteLine("V5.4 balance/visual checks: "+checks+" passed. Simulated combat only; no real cleanup, memory trim or persistence.");
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
