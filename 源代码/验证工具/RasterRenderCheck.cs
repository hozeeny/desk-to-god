using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
namespace NetBunny
{
    internal static class RasterRenderCheck
    {
        [STAThread] private static int Main(string[] args)
        {
            try
            {
                string assets = args.Length > 0 ? args[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
                string output = args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "render-check.png");
                if (args.Length > 2 && args[2] == "gif-only") { AdventureRenderer.ExportRunGif(assets, output); return 0; }
                AdventureRenderer.ExportPreview(assets, output);
                Console.WriteLine(AdventureRenderer.SelfTest());
                Console.WriteLine(AdventureRenderer.AssetsSelfTest(assets));
                AdventureRenderer.ExportAnimationFrames(assets, Path.Combine(Path.GetDirectoryName(output), "run-frames"));
                string gif = Path.Combine(Path.GetDirectoryName(output), "骑士连续跑步.gif"); AdventureRenderer.ExportRunGif(assets, gif);
                using (Image animation = Image.FromFile(gif))
                {
                    int count = animation.GetFrameCount(System.Drawing.Imaging.FrameDimension.Time);
                    if (count != 60) throw new InvalidOperationException("Exported GIF lost frames: " + count);
                    for (int i = 0; i < count; i++) animation.SelectActiveFrame(System.Drawing.Imaging.FrameDimension.Time, i);
                    Console.WriteLine("Animated GIF: 60 frames decoded; 50 fps GIF preview; runtime targets 15 ms timer ticks, actual display rate varies.");
                }
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
