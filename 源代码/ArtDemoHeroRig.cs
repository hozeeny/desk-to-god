using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Web.Script.Serialization;

namespace NetBunny
{
    // Opt-in source-pixel rig. Uses the existing gait/IK solver, not sprite-frame
    // playback or a translated complete portrait. The original renderer is untouched.
    internal sealed class ArtDemoHeroRig : IDisposable
    {
        internal const double StrideScale = .62;
        internal const double TravelPerCycle = HeroPoseRig.TravelPerCycle * StrideScale;
        public sealed class Part { public string name {get;set;} public float[] bounds {get;set;} public float[] sourceBounds {get;set;} }
        public sealed class Definition { public int version {get;set;} public int[] canvas {get;set;} public Part[] parts {get;set;} public Dictionary<string,float[]> joints {get;set;} public string sourceSha256 {get;set;} }
        readonly RasterAtlas atlas;
        readonly Bitmap[] parts = new Bitmap[11];
        readonly Definition definition;
        readonly RectangleF[] bounds = new RectangleF[11];
        readonly Dictionary<string,PointF> joints = new Dictionary<string,PointF>();
        bool disposed;

        internal ArtDemoHeroRig(string directory, string stage)
        {
            string path = Path.Combine(directory, "hero-rig-" + stage + ".png");
            string anchors = Path.ChangeExtension(path, ".anchors"), rects = Path.ChangeExtension(path, ".rects");
            if (!File.Exists(path) || !File.Exists(anchors) || !File.Exists(rects)) throw new InvalidDataException("缺少新角色分件图集/rects/anchors：" + stage);
            try
            {
                definition = new JavaScriptSerializer().Deserialize<Definition>(File.ReadAllText(anchors));
                if (definition == null || definition.version != 1 || definition.parts == null || definition.parts.Length != 12 || definition.canvas.Length != 2 || definition.canvas[0] != 220 || definition.canvas[1] != 240) throw new InvalidDataException("角色锚点版本或部件数无效：" + anchors);
                foreach (KeyValuePair<string,float[]> pair in definition.joints) { if (pair.Value.Length != 2 || !Finite(pair.Value[0]) || !Finite(pair.Value[1])) throw new InvalidDataException("角色锚点坐标无效：" + pair.Key); joints.Add(pair.Key,new PointF(pair.Value[0],pair.Value[1])); }
                foreach (string key in new[]{"neck","nearShoulder","nearElbow","nearHand","farShoulder","farElbow","farHand","nearHip","nearKnee","nearAnkle","nearSole","farHip","farKnee","farAnkle","farSole","tailRoot"}) if (!joints.ContainsKey(key)) throw new InvalidDataException("角色锚点缺失：" + key);
                atlas = new RasterAtlas(path,4,3);
                if (atlas.Image.Width != 1024 || atlas.Image.Height != 768 || atlas.TransparentRatio < .4) throw new InvalidDataException("角色图集网格或透明通道无效：" + path);
                for (int i=0;i<12;i++) if (!atlas.HasCell(i)) throw new InvalidDataException("角色图集空部件：" + i);
                for (int i=0;i<11;i++)
                {
                    float[] b=definition.parts[i].bounds;
                    if (b == null || b.Length != 4 || !Finite(b[0]) || !Finite(b[1]) || !Finite(b[2]) || !Finite(b[3]) || b[2] <= 0 || b[3] <= 0) throw new InvalidDataException("角色部件边界无效：" + i);
                    bounds[i]=new RectangleF(b[0],b[1],b[2],b[3]);
                    Rectangle r=atlas.Cells[i]; parts[i]=new Bitmap(r.Width,r.Height,PixelFormat.Format32bppPArgb);
                    using(Graphics g=Graphics.FromImage(parts[i])) {g.CompositingMode=CompositingMode.SourceCopy;g.DrawImage(atlas.Image,new Rectangle(0,0,r.Width,r.Height),r,GraphicsUnit.Pixel);}
                }
            }
            catch { Dispose(); throw; }
        }
        static bool Finite(float x) {return !Single.IsNaN(x) && !Single.IsInfinity(x);}
        static double Clamp(double x) {return Double.IsNaN(x)?0:Math.Max(0,Math.Min(1,x));}
        static float Distance(PointF a,PointF b) {float x=a.X-b.X,y=a.Y-b.Y;return (float)Math.Sqrt(x*x+y*y);}
        static PointF Offset(PointF p,float x,float y) {return new PointF(p.X+x,p.Y+y);}
        PointF Joint(string key) {return joints[key];}
        static PointF BodyPoint(PointF p,float lean,float bob,float lunge)
        {
            double a=lean*Math.PI/180,x=p.X-110,y=p.Y-130;
            return new PointF((float)(110+x*Math.Cos(a)-y*Math.Sin(a))+lunge,(float)(130+x*Math.Sin(a)+y*Math.Cos(a))+bob);
        }
        static void BodyTransform(Graphics g,float lean,float bob,float lunge) {g.TranslateTransform(110+lunge,130+bob);g.RotateTransform(lean);g.TranslateTransform(-110,-130);}
        void DrawPart(Graphics g,int index,float opacity,float flash,RectangleF? region)
        {
            RectangleF b=bounds[index], clip=region??b;
            clip=RectangleF.Intersect(b,clip);if(clip.Width<=0||clip.Height<=0)return;
            Bitmap image=parts[index];RectangleF src=new RectangleF((clip.X-b.X)/b.Width*image.Width,(clip.Y-b.Y)/b.Height*image.Height,clip.Width/b.Width*image.Width,clip.Height/b.Height*image.Height);
            DrawImage(g,image,clip,src,opacity,flash);
        }
        static void DrawImage(Graphics g,Bitmap image,RectangleF target,RectangleF source,float opacity,float flash)
        {
            if(opacity>=.999f&&flash<=0)
            {
                InterpolationMode old=g.InterpolationMode;g.InterpolationMode=InterpolationMode.Bilinear;
                g.DrawImage(image,target,source,GraphicsUnit.Pixel);g.InterpolationMode=old;return;
            }
            using(ImageAttributes attributes=new ImageAttributes())
            {
                float light=Math.Max(0,Math.Min(.65f,flash));ColorMatrix matrix=new ColorMatrix();matrix.Matrix00=matrix.Matrix11=matrix.Matrix22=1-light;matrix.Matrix40=matrix.Matrix41=matrix.Matrix42=light;matrix.Matrix33=Math.Max(0,Math.Min(1,opacity));attributes.SetColorMatrix(matrix);
                PointF[] p={new PointF(target.Left,target.Top),new PointF(target.Right,target.Top),new PointF(target.Left,target.Bottom)};
                InterpolationMode old=g.InterpolationMode;g.InterpolationMode=InterpolationMode.Bilinear;g.DrawImage(image,p,source,GraphicsUnit.Pixel,attributes);g.InterpolationMode=old;
            }
        }
        void Segment(Graphics g,int index,PointF fromSource,PointF toSource,PointF from,PointF to,float opacity,float flash,RectangleF? clip)
        {
            float scale=Distance(from,to)/Math.Max(.1f,Distance(fromSource,toSource));
            GraphicsState saved=g.Save();g.TranslateTransform(from.X,from.Y);g.RotateTransform((float)((Math.Atan2(to.Y-from.Y,to.X-from.X)-Math.Atan2(toSource.Y-fromSource.Y,toSource.X-fromSource.X))*180/Math.PI));g.ScaleTransform(scale,scale);g.TranslateTransform(-fromSource.X,-fromSource.Y);
            DrawPart(g,index,opacity,flash,clip);g.Restore(saved);
        }
        void DrawLeg(Graphics g,bool far,PointF hip,HeroFootPose gait,bool running,bool rest,float opacity,float flash)
        {
            string prefix=far?"far":"near";PointF baseAnkle=Joint(prefix+"Ankle"),ankle=FootPosition(far,gait,running);
            float upper=Distance(Joint(prefix+"Hip"),Joint(prefix+"Knee")),lower=Distance(Joint(prefix+"Knee"),baseAnkle);
            // Same two-bone IK as the established rig. Authoring lengths differ per skin.
            PointF knee=HeroPoseRig.Joint(hip,ankle,upper,lower,1);
            // The authored wide trousers do not contain unseen folded surfaces.
            // Quiet recovery therefore keeps the original planted leg geometry,
            // with a restrained whole-body slump and breathing, not a fake fold.
            if(rest) {hip=Joint(prefix+"Hip");knee=Joint(prefix+"Knee");ankle=baseAnkle;}
            int thigh=far?8:6,shin=far?9:7;
            Segment(g,thigh,Joint(prefix+"Hip"),Joint(prefix+"Knee"),hip,knee,opacity,flash,null);
            RectangleF b=bounds[shin];float cut=baseAnkle.Y-2;
            Segment(g,shin,Joint(prefix+"Knee"),baseAnkle,knee,ankle,opacity,flash,new RectangleF(b.X,b.Y,b.Width,baseAnkle.Y+2-b.Y));
            GraphicsState foot=g.Save();g.TranslateTransform(ankle.X,ankle.Y);g.RotateTransform(running?(float)gait.FootAngle:0);g.TranslateTransform(-baseAnkle.X,-baseAnkle.Y);
            DrawPart(g,shin,opacity,flash,new RectangleF(b.X,cut,b.Width,b.Bottom-cut));g.Restore(foot);
        }
        PointF FootPosition(bool far,HeroFootPose gait,bool running)
        {
            string prefix=far?"far":"near";PointF ankle=Joint(prefix+"Ankle");
            if(running)
            {
                // Centre the stride on the authored hip, rather than on a boot
                // already at full extension in the reference standing pose.
                ankle.X=Joint(prefix+"Hip").X+(float)((gait.Ankle.X-(far?120:104))*StrideScale);
                ankle.Y+=(float)((gait.Ankle.Y-(far?178:181))*.65);
            }
            return ankle;
        }
        internal void Draw(Graphics g,AdventureRenderState s,Bitmap weapon)
        {
            if(disposed)throw new ObjectDisposedException("ArtDemoHeroRig");
            string phase=(s.Phase??"idle").ToLowerInvariant();bool run=phase=="run"||phase=="running", attack=phase=="attack"||s.AttackProgress>.03, guard=phase=="guard"||phase=="block", pant=phase=="pant"||phase=="panting";
            bool rest=phase=="grave"||phase=="dead"||phase=="tombstone",dying=phase=="dying"||phase=="death",revive=phase=="revive"||phase=="reviving";
            double a=RasterActorRenderer.NormalizedCycle(s)*Math.PI*2,push=attack?Math.Sin(Clamp(s.AttackProgress)*Math.PI):0;
            float bob=(float)(run?Math.Cos(a*2)*1.0:Math.Sin(s.Time*(pant?5:2.5))*(pant?.8:.35)),lean=(float)(run?3.5+Math.Sin(a)*.7:attack?-push*3:guard?-3:0),lunge=(float)(push*5);
            float opacity=dying?(float)(1-Clamp(s.DeathProgress>0?s.DeathProgress:s.PhaseTime/3.4)*.55):1,flash=(float)Clamp(s.HitFlash)*.55f;
            HeroPose gait=HeroPoseRig.Evaluate(s,new HeroWeaponGrip {Width=40,Height=92,Main=new PointF(.5f,.85f),TwoHands=false});
            GraphicsState root=g.Save();
            if(!s.FacingRight){g.TranslateTransform(220,0);g.ScaleTransform(-1,1);}
            if(s.HitFlash>0)g.TranslateTransform((float)(-Math.Sin(s.Time*51)*Math.Min(4,s.HitFlash*4)),0);
            if(rest||dying){float p=rest?1:(float)Clamp(s.DeathProgress);g.TranslateTransform(110,185);g.RotateTransform((rest?-3:-10)*p);g.ScaleTransform(1-.02f*p,1-(rest?.06f:.13f)*p);g.TranslateTransform(-110,-185);if(!rest)bob+=8*p;}
            if(revive){float p=(float)Clamp(s.DeathProgress>0?s.DeathProgress:s.PhaseTime/2.4);opacity=.3f+.7f*p;bob+=(1-p)*13;}
            PointF nearHip=BodyPoint(Joint("nearHip"),lean,bob,lunge),farHip=BodyPoint(Joint("farHip"),lean,bob,lunge);
            // Far silhouettes remain behind torso. No complete reference cell is rendered.
            DrawLeg(g,true,farHip,gait.FarFoot,run,rest,opacity,flash);
            DrawLeg(g,false,nearHip,gait.NearFoot,run,rest,opacity,flash);
            PointF nearShoulder=BodyPoint(Joint("nearShoulder"),lean,bob,lunge),farShoulder=BodyPoint(Joint("farShoulder"),lean,bob,lunge);
            PointF nearHand=BodyPoint(Joint("nearHand"),lean,bob,lunge),farHand=BodyPoint(Joint("farHand"),lean,bob,lunge);
            if(run){nearHand=Offset(nearHand,(float)Math.Sin(a)*5,(float)Math.Sin(a)*-2);farHand=Offset(farHand,(float)Math.Sin(a)*-3,(float)Math.Sin(a)*2);}
            if(attack){farHand=Offset(farHand,(float)push*9,(float)-push*22);nearHand=Offset(nearHand,(float)push*13,(float)-push*8);}
            if(guard){farHand=Offset(farHand,-12,-29);nearHand=Offset(nearHand,25,-23);}
            if(rest){nearHand=Offset(nearHand,13,-4);farHand=Offset(farHand,-7,-3);}
            PointF nearElbow=HeroPoseRig.Joint(nearShoulder,nearHand,Distance(Joint("nearShoulder"),Joint("nearElbow")),Distance(Joint("nearElbow"),Joint("nearHand")),-1);
            PointF farElbow=HeroPoseRig.Joint(farShoulder,farHand,Distance(Joint("farShoulder"),Joint("farElbow")),Distance(Joint("farElbow"),Joint("farHand")),1);
            Segment(g,4,Joint("farShoulder"),Joint("farElbow"),farShoulder,farElbow,opacity,flash,null);
            GraphicsState body=g.Save();BodyTransform(g,lean,bob,lunge);
            GraphicsState tail=g.Save();PointF tailRoot=Joint("tailRoot");g.TranslateTransform(tailRoot.X,tailRoot.Y);g.RotateTransform((float)(Math.Sin(a-.5)*(run?3:1)));g.TranslateTransform(-tailRoot.X,-tailRoot.Y);DrawPart(g,10,opacity,flash,null);g.Restore(tail);
            DrawPart(g,1,opacity,flash,null);
            PointF neck=Joint("neck");GraphicsState head=g.Save();g.TranslateTransform(neck.X,neck.Y);g.RotateTransform((float)(-lean*.55+(pant?4:0)));g.TranslateTransform(-neck.X,-neck.Y);DrawPart(g,0,opacity,flash,null);g.Restore(head);g.Restore(body);
            Segment(g,2,Joint("nearShoulder"),Joint("nearElbow"),nearShoulder,nearElbow,opacity,flash,null);
            if(!rest&&!dying)
            {
                GraphicsState held=g.Save();g.TranslateTransform(farHand.X,farHand.Y);g.RotateTransform((float)(guard?58:attack?12+push*24:12+Math.Sin(a)*(run?4:1)));g.TranslateTransform(-farHand.X,-farHand.Y);
                RectangleF box=ArtDemoPack.HeldWeaponBounds(weapon.Size,farHand);DrawImage(g,weapon,box,new RectangleF(0,0,weapon.Width,weapon.Height),opacity,0);g.Restore(held);
            }
            Segment(g,5,Joint("farElbow"),Joint("farHand"),farElbow,farHand,opacity,flash,null);
            Segment(g,3,Joint("nearElbow"),Joint("nearHand"),nearElbow,nearHand,opacity,flash,null);
            g.Restore(root);
        }
        internal void Validate(Action<bool,string> check)
        {
            check(atlas.Cells.Length==12&&parts.Length==11,"4x3 atlas has eleven independently cached body parts and one reference");
            for(int i=0;i<12;i++)
            {
                Rectangle r=atlas.Cells[i];check(atlas.HasCell(i),"body source cell "+i+" contains authored alpha");
                check(r.X/256==i%4&&r.Y/256==i/4&&(r.Right-1)/256==i%4&&(r.Bottom-1)/256==i/4,"rects remain inside their own grid cell "+i);
                check(r.X%256>=8&&r.Y%256>=8&&256-r.Right%256>=8&&256-r.Bottom%256>=8,"part "+i+" keeps eight atlas pixels safe margin");
            }
            check(Joint("nearSole").Y<190&&Joint("farSole").Y<190,"soles remain above HP and caption strip");
            check(TravelPerCycle>40&&TravelPerCycle<60,"short-legged skin publishes distance-matched gait period");
            HeroPose previous=null;double previousNear=0,previousFar=0;
            for(int n=0;n<=240;n++)
            {
                double cycle=n/240.0;HeroPose gait=HeroPoseRig.Evaluate(new AdventureRenderState{Phase="run",Cycle=cycle,Time=0},new HeroWeaponGrip{TwoHands=false});
                PointF near=FootPosition(false,gait.NearFoot,true),far=FootPosition(true,gait.FarFoot,true);
                double nearWorld=TravelPerCycle*cycle+near.X,farWorld=TravelPerCycle*cycle+far.X;
                check(near.Y<=Joint("nearAnkle").Y+.001&&far.Y<=Joint("farAnkle").Y+.001,"both soles stay on or above the authored floor");
                if(gait.NearFoot.Planted)check(Math.Abs(near.Y-Joint("nearAnkle").Y)<.001,"near support ankle stays at contact height");
                if(gait.FarFoot.Planted)check(Math.Abs(far.Y-Joint("farAnkle").Y)<.001,"far support ankle stays at contact height");
                if(previous!=null)
                {
                    if(previous.NearFoot.Planted&&gait.NearFoot.Planted&&gait.NearFoot.Phase>previous.NearFoot.Phase)check(Math.Abs(nearWorld-previousNear)<.0001,"near foot has no world-space stance slide at the published cycle distance");
                    if(previous.FarFoot.Planted&&gait.FarFoot.Planted&&gait.FarFoot.Phase>previous.FarFoot.Phase)check(Math.Abs(farWorld-previousFar)<.0001,"far foot has no world-space stance slide at the published cycle distance");
                }
                previous=gait;previousNear=nearWorld;previousFar=farWorld;
            }
        }
        public void Dispose(){if(disposed)return;disposed=true;foreach(Bitmap b in parts)if(b!=null)b.Dispose();if(atlas!=null)atlas.Dispose();}
    }
}
