using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace NetBunny
{
    // Independent, mouse-transparent projectiles. No file or game-state authority.
    internal sealed class AdventureEffects : IDisposable
    {
        sealed class Shot : IDisposable
        {
            internal PointF From, To;
            internal string Weapon;
            internal double Age, Duration;
            internal float Scale;
            internal KnightLayeredWindow Window = new KnightLayeredWindow { PassThrough = true };
            public void Dispose() { Window.Dispose(); }
        }
        readonly List<Shot> shots = new List<Shot>();
        double repaint;
        bool disposed;
        internal int ActiveCount { get { return shots.Count; } }
        internal void Fire(PointF origin, PointF target, string weaponId, float scale)
        { FireCore(origin, target, weaponId, scale, 3); }
        internal void FireField(PointF origin, IEnumerable<PointF> targets, float scale)
        {
            if (disposed || targets == null) return;
            Clear(); int count = 0;
            foreach (PointF target in targets) { if (count++ >= 13) break; FireCore(origin, target, "blackhole", scale, 13); }
        }
        private void FireCore(PointF origin, PointF target, string weaponId, float scale, int limit)
        {
            if (disposed) return;
            while (shots.Count >= limit) { shots[0].Dispose(); shots.RemoveAt(0); }
            Shot s = new Shot { From = origin, To = target, Weapon = weaponId, Scale = Math.Max(.5f, Math.Min(2f, scale)), Duration = weaponId == "boomerang" ? .85 : weaponId == "blackhole" ? .9 : weaponId == "laser" ? .26 : .5 };
            s.Window.Text = "骑士助手 · 武器特效"; shots.Add(s); Paint(s);
        }
        internal void Tick(double dt)
        {
            if (disposed) return;
            dt = Math.Max(0, Math.Min(.2, dt)); repaint += dt;
            for (int i = shots.Count - 1; i >= 0; --i)
            {
                Shot s = shots[i]; s.Age += dt;
                if (s.Age >= s.Duration) { s.Dispose(); shots.RemoveAt(i); }
                else if (repaint >= .025) Paint(s);
            }
            if (repaint >= .025) repaint = 0;
        }
        static void Paint(Shot s)
        {
            double progress = Math.Min(1, s.Age / s.Duration);
            if (s.Weapon == "laser")
            {
                // A beam spans its real source and target, with a narrow transparent HWND.
                int pad = 24;
                Rectangle r = Rectangle.FromLTRB((int)Math.Min(s.From.X, s.To.X) - pad, (int)Math.Min(s.From.Y, s.To.Y) - pad, (int)Math.Max(s.From.X, s.To.X) + pad, (int)Math.Max(s.From.Y, s.To.Y) + pad);
                // Bound transient allocation even on a very large multi-monitor desktop.
                if (r.Width > 2400 || r.Height > 1500) { PaintProjectile(s, progress); return; }
                Bitmap frame = new Bitmap(Math.Max(1,r.Width), Math.Max(1,r.Height), PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(frame))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    PointF a = new PointF(s.From.X-r.X,s.From.Y-r.Y), b = new PointF(s.To.X-r.X,s.To.Y-r.Y);
                    for (int i = 3; i >= 0; --i)
                        using (Pen p = new Pen(Color.FromArgb((int)((i==0?240:50)*(1-progress)),i==0?Color.White:Color.Cyan),(i*6+2)*s.Scale)) { p.StartCap=p.EndCap=LineCap.Round;g.DrawLine(p,a,b); }
                    using (Brush glow = new SolidBrush(Color.FromArgb((int)(180*(1-progress)),Color.White))) g.FillEllipse(glow,b.X-9,b.Y-9,18,18);
                }
                s.Window.Location=r.Location;s.Window.Present(frame);if(!s.Window.Visible)s.Window.Show();return;
            }
            PaintProjectile(s,progress);
        }
        static void PaintProjectile(Shot s,double progress)
        {
            double t=s.Weapon=="boomerang" ? Math.Sin(progress*Math.PI) : Math.Min(1,progress*1.5);
            if(s.Weapon=="blackhole")t=1;
            float x=(float)(s.From.X+(s.To.X-s.From.X)*t),y=(float)(s.From.Y+(s.To.Y-s.From.Y)*t);
            int size=(int)(192*s.Scale);Bitmap frame=new Bitmap(size,size,PixelFormat.Format32bppPArgb);
            using(Graphics g=Graphics.FromImage(frame))
            {
                g.SmoothingMode=SmoothingMode.AntiAlias;g.TranslateTransform(size/2f,size/2f);g.ScaleTransform(s.Scale,s.Scale);
                float fade=(float)Math.Min(1,(1-progress)*4);
                if(s.Weapon=="blackhole")
                {
                    float radius=(float)(22+32*Math.Sin(progress*Math.PI));
                    for(int i=5;i>=0;i--)using(Pen p=new Pen(Color.FromArgb((int)((30+i*16)*fade),i%2==0?Color.MediumPurple:Color.DeepSkyBlue),3))
                    {float r=radius+i*5;g.DrawArc(p,-r,-r,r*2,r*2,(float)(progress*720+i*37),240);}
                    using(Brush b=new SolidBrush(Color.FromArgb((int)(245*fade),Color.FromArgb(17,11,36))))g.FillEllipse(b,-radius,-radius,radius*2,radius*2);
                    for(int i=0;i<9;i++){double a=i*Math.PI*2/9+progress*10;float rr=(float)(76*(1-progress));using(Pen p=new Pen(Color.FromArgb((int)(220*fade),Color.LightCyan),3))g.DrawLine(p,(float)Math.Cos(a)*rr,(float)Math.Sin(a)*rr,(float)Math.Cos(a+.16)*(rr+10),(float)Math.Sin(a+.16)*(rr+10));}
                }
                else if(s.Weapon=="boomerang")
                {
                    g.RotateTransform((float)(progress*1080));
                    for(int i=3;i>=0;i--)using(Pen p=new Pen(Color.FromArgb((int)((35+i*18)*fade),Color.Aquamarine),3+i*3))g.DrawArc(p,-42,-42,84,84,i*12,210);
                    using(GraphicsPath p=new GraphicsPath()){p.AddPolygon(new[]{new PointF(-44,12),new PointF(-5,-30),new PointF(43,8),new PointF(9,-6),new PointF(0,20),new PointF(-12,3)});using(Brush b=new SolidBrush(Color.FromArgb((int)(255*fade),Color.Gold)))g.FillPath(b,p);using(Pen pen=new Pen(Color.White,2))g.DrawPath(pen,p);}
                }
                else if(s.Weapon=="wave")
                {
                    g.RotateTransform((float)(Math.Atan2(s.To.Y-s.From.Y,s.To.X-s.From.X)*180/Math.PI));
                    for(int i=4;i>=0;i--)using(Pen p=new Pen(Color.FromArgb((int)((150-i*25)*fade),i%2==0?Color.Aqua:Color.MediumPurple),4+i*2))g.DrawArc(p,-40-i*9,-42,70,84,-75,150);
                }
                else if(s.Weapon=="automaton")
                {
                    for(int i=3;i>=0;i--)using(Brush b=new SolidBrush(Color.FromArgb((int)((i==0?255:45)*fade),i==0?Color.White:Color.Orange)))g.FillEllipse(b,-(7+i*7),-(7+i*7),14+i*14,14+i*14);
                }
                else
                {
                    float r=(float)(25+progress*38);using(Pen p=new Pen(Color.FromArgb((int)(230*fade),s.Weapon=="greatsword"?Color.Orchid:Color.LightGoldenrodYellow),s.Weapon=="greatsword"?9:4))g.DrawArc(p,-r,-r,r*2,r*2,(float)(-100+progress*170),130);
                    for(int i=0;i<5;i++){double a=i*1.256+progress;using(Pen p=new Pen(Color.FromArgb((int)(200*fade),Color.White),2))g.DrawLine(p,(float)Math.Cos(a)*r*.6f,(float)Math.Sin(a)*r*.6f,(float)Math.Cos(a)*r,(float)Math.Sin(a)*r);}
                }
            }
            s.Window.Location=new Point((int)x-size/2,(int)y-size/2);s.Window.Present(frame);if(!s.Window.Visible)s.Window.Show();
        }
        internal void Clear() { foreach(Shot s in shots)s.Dispose();shots.Clear(); }
        public void Dispose() { if(disposed)return;disposed=true;Clear(); }
    }
}
