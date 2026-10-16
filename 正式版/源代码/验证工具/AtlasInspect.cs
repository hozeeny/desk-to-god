using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
internal static class AtlasInspect
{
    sealed class Part {internal int X,Y,R,B,Pixels;internal double CenterY{get{return (Y+B)*.5;}}}
    static int Main(string[] args)
    {
        try
        {
            string file=args[0];int columns=int.Parse(args[1]),rows=int.Parse(args[2]);
            using(Bitmap b=new Bitmap(file))
            {
                var d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                byte[] bytes=new byte[Math.Abs(d.Stride)*b.Height];Marshal.Copy(d.Scan0,bytes,0,bytes.Length);b.UnlockBits(d);
                int w=b.Width,h=b.Height,stride=Math.Abs(d.Stride);bool[] seen=new bool[w*h];int[] queue=new int[w*h];List<Part> parts=new List<Part>();
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                {
                    int index=y*w+x;if(seen[index]||bytes[y*stride+x*4+3]<16)continue;
                    int read=0,write=1;queue[0]=index;seen[index]=true;Part p=new Part{X=x,Y=y,R=x,B=y};
                    while(read<write)
                    {
                        int at=queue[read++],xx=at%w,yy=at/w;p.Pixels++;p.X=Math.Min(p.X,xx);p.Y=Math.Min(p.Y,yy);p.R=Math.Max(p.R,xx);p.B=Math.Max(p.B,yy);
                        for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int nx=xx+dx,ny=yy+dy;if(nx<0||ny<0||nx>=w||ny>=h)continue;int ni=ny*w+nx;if(seen[ni]||bytes[ny*stride+nx*4+3]<16)continue;seen[ni]=true;queue[write++]=ni;}
                    }
                    if(p.Pixels>400&&p.R-p.X>12&&p.B-p.Y>12)parts.Add(p);
                }
                Console.WriteLine(Path.GetFileName(file)+": "+w+"x"+h+" components="+parts.Count+" expected="+(columns*rows)+" alphaCorner="+bytes[3]);
                List<Part> sorted=parts.OrderBy(p=>p.CenterY).ToList();List<string> lines=new List<string>();
                for(int row=0;row<rows;row++)foreach(Part p in sorted.Skip(row*columns).Take(columns).OrderBy(p=>p.X))
                {string line=Math.Max(0,p.X-3)+","+Math.Max(0,p.Y-3)+","+(Math.Min(w-1,p.R+3)-Math.Max(0,p.X-3)+1)+","+(Math.Min(h-1,p.B+3)-Math.Max(0,p.Y-3)+1);Console.WriteLine(line+" pixels="+p.Pixels);lines.Add(line);}
                if(parts.Count==columns*rows)File.WriteAllLines(Path.ChangeExtension(file,".rects"),lines);
                else {Console.WriteLine("MISMATCH: no metadata written");return 2;}
            }
            return 0;
        }
        catch(Exception e){Console.WriteLine(e);return 1;}
    }
}
