using System;
using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using NetBunny;

// Compile beside DesktopFrameCapture.cs. No capture or visible UI is invoked.
// This verifies the actors-hidden white background exception without relaxing
// either published-frame detail checks or the real-actor pixel verifier.
internal static class VerifyCaptureBackground
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Expected game EXE and actual transparent hero PNG.");
            Assembly game = Assembly.LoadFrom(args[0]);
            Type actorType = game.GetType("NetBunny.ArtDemoActorFrame", true);
            Type verifier = game.GetType("NetBunny.ArtDemoPixelVerifier", true);
            MethodInfo verify = verifier.GetMethod("VerifyActor", BindingFlags.Static | BindingFlags.NonPublic);
            using (Bitmap background = new Bitmap(220, 240, PixelFormat.Format24bppRgb))
            using (Bitmap sprite = new Bitmap(220, 240, PixelFormat.Format32bppArgb))
            using (Image source = Image.FromFile(args[1]))
            {
                using (Graphics g = Graphics.FromImage(background)) g.Clear(Color.White);
                using (Graphics g = Graphics.FromImage(sprite))
                {
                    float scale = Math.Min(204f / source.Width, 224f / source.Height);
                    float width = source.Width * scale, height = source.Height * scale;
                    g.DrawImage(source, new RectangleF((220 - width) / 2, 232 - height, width, height));
                }
                if (DesktopFrameCapture.HasVisibleDetail(background)) throw new InvalidOperationException("White-background reproduction unexpectedly has detail.");
                bool strictRejected = false;
                try { DesktopFrameCapture.ValidateCapturedFrame(background, true); }
                catch (InvalidOperationException) { strictRejected = true; }
                if (!strictRejected) throw new InvalidOperationException("Published uniform screenshot incorrectly accepted.");
                DesktopFrameCapture.ValidateCapturedFrame(background, false);
                object actor = Activator.CreateInstance(actorType, true);
                Action<string, object> set = delegate(string name, object value) { actorType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(actor, value); };
                set("Frame", sprite); set("Bounds", new Rectangle(0, 0, 220, 240)); set("Role", "actual-hero-on-white"); set("ZOrder", 0);
                IList actors = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(actorType)); actors.Add(actor);
                using (Bitmap composite = new Bitmap(background))
                {
                    using (Graphics g = Graphics.FromImage(composite)) g.DrawImageUnscaled(sprite, 0, 0);
                    DesktopFrameCapture.ValidateCapturedFrame(composite, true);
                    verify.Invoke(null, new[] { actor, actors, composite, background, composite, (object)Point.Empty });
                    bool absentRejected = false;
                    try { verify.Invoke(null, new[] { actor, actors, background, background, composite, (object)Point.Empty }); }
                    catch (TargetInvocationException error) { if (error.InnerException is InvalidOperationException) absentRejected = true; else throw; }
                    if (!absentRejected) throw new InvalidOperationException("White screenshot without the expected real actor incorrectly accepted.");
                }
                verifier.GetMethod("TransparentNegativeCheck", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                Console.WriteLine("Capture background regression: 6 checks passed; uniform hidden-actor background accepted, real hero composite accepted, published uniform/absent hero/transparent frames rejected. No GUI or screen capture.");
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
