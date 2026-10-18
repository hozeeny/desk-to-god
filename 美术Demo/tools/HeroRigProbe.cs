using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
namespace NetBunny {
 internal static class HeroRigProbe {
  static int Main(string[] args) {
   try {
    if(args.Length<3 || args[2]!="preview-only") Console.WriteLine(AdventureRenderer.ArtDemoSelfTest(args[0]));
    if(args.Length > 1) {
     Directory.CreateDirectory(args[1]);
     string[] phases={"idle","run","run","attack","guard","pant","grave","revive"};
     using(var renderer=new AdventureRenderer("",args[0]))
     using(var sheet=new Bitmap(220*8,270*6,PixelFormat.Format32bppArgb))
     using(var g=Graphics.FromImage(sheet))
     using(var font=new Font("Microsoft YaHei UI",12,GraphicsUnit.Pixel)) {
      for(int row=0;row<6;row++) {
       string stage=new[]{"mortal","qishi","zhuji"}[row%3];
       for(int col=0;col<8;col++) {
        var rect=new Rectangle(col*220,row*270,220,270);
        g.FillRectangle(row<3?Brushes.Beige:Brushes.DarkSlateGray,rect);
        var s=new AdventureRenderState {HeroStage=stage,Phase=phases[col],Time=.6,Cycle=col==2?.65:.15,AttackProgress=phases[col]=="attack"?.5:0,Size=new Size(220,240),DeathProgress=.45,Label="",HealthRatio=1};
        using(var frame=renderer.Render(s)) {g.DrawImageUnscaled(frame,col*220,row*270+20);frame.Save(Path.Combine(args[1],stage+"-"+col+"-"+phases[col]+"-"+(row<3?"light":"dark")+".png"),ImageFormat.Png);}
        g.DrawString(stage+" / "+phases[col]+(col==2?" .65":""),font,row<3?Brushes.Black:Brushes.White,col*220+8,row*270+4);
       }
      }
      sheet.Save(Path.Combine(args[1],"three-stage-actions-220x240-light-dark.png"),ImageFormat.Png);
     }
    }
    return 0;
   }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
  }
 }
}
