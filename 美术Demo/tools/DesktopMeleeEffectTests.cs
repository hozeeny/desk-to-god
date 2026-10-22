using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace NetBunny
{
    // Pure in-memory production raster paths. Reflection temporarily substitutes
    // one same-sized transparent cache entry; no production testing seam, forms,
    // actor windows, controllers, archives, observers or system work are used.
    internal static class DesktopMeleeEffectTests
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static readonly List<CheckResult> assertions = new List<CheckResult>();
        static readonly List<FrameResult> frames = new List<FrameResult>();
        static int failures;
        sealed class Choice
        {
            internal string Name, Id, Asset;
            internal bool Melee;
            internal Choice(string name, string id, string asset, bool melee)
            { Name = name; Id = id; Asset = asset; Melee = melee; }
        }
        public sealed class CheckResult { public bool passed; public string detail; }
        public sealed class FrameResult
        {
            public string key, kind, input, mappedAsset, originalRawSha256, withoutWeaponRawSha256, restoredRawSha256;
            public bool melee;
            public int size, visiblePixels, transparentPixels, translucentPixels, textureDependentPixels;
            public double progress;
        }
        public sealed class Evidence
        {
            public bool passed, baselineProvided;
            public int checks, failures;
            public double seconds;
            public string error, scope, baselineReportSha256;
            public Dictionary<string, string> assetHashes;
            public List<FrameResult> frames;
            public List<CheckResult> assertions;
        }
        static readonly Choice[] choices = {
            new Choice("null", null, "weapon-mortal", true),
            new Choice("empty", "", "weapon-mortal", true),
            new Choice("whitespace", "  ", "weapon-mortal", true),
            new Choice("spear", "spear", "weapon-mortal", true),
            new Choice("mortal", "mortal", "weapon-mortal", true),
            new Choice("spear-trim-case", "  SpEaR  ", "weapon-mortal", true),
            new Choice("mortal-trim-case", "  MoRtAl  ", "weapon-mortal", true),
            new Choice("greatsword", "greatsword", "weapon-zhuji", true),
            new Choice("zhuji", "zhuji", "weapon-zhuji", true),
            new Choice("greatsword-trim-case", "  GrEaTsWoRd  ", "weapon-zhuji", true),
            new Choice("zhuji-trim-case", "  ZhUjI  ", "weapon-zhuji", true),
            new Choice("laser", "laser", "weapon-qishi", false),
            new Choice("qishi", "qishi", "weapon-qishi", false),
            new Choice("laser-trim-case", "  LaSeR  ", "weapon-qishi", false),
            new Choice("qishi-trim-case", "  QiShI  ", "weapon-qishi", false),
            new Choice("wave", "wave", "weapon-wave", false),
            new Choice("wave-trim-case", "  WaVe  ", "weapon-wave", false),
            new Choice("boomerang", "boomerang", "weapon-moonring", false),
            new Choice("moonring", "moonring", "weapon-moonring", false),
            new Choice("moonring-trim-case", "  MoOnRiNg  ", "weapon-moonring", false),
            new Choice("blackhole", "blackhole", "weapon-array", false),
            new Choice("array", "array", "weapon-array", false),
            new Choice("array-trim-case", "  ArRaY  ", "weapon-array", false),
            new Choice("automaton", "automaton", "weapon-talisman", false),
            new Choice("talisman", "talisman", "weapon-talisman", false),
            new Choice("talisman-trim-case", "  TaLiSmAn  ", "weapon-talisman", false)
        };
        static void Check(bool okay, string detail)
        {
            assertions.Add(new CheckResult { passed = okay, detail = detail });
            if (!okay) failures++;
            Console.WriteLine((okay ? "PASS " : "FAIL ") + detail);
            Deadline();
        }
        static void Deadline()
        { if (clock.Elapsed.TotalSeconds > 18) throw new TimeoutException("Pure raster fixture exceeded its own 18-second deadline."); }
        static string Hash(byte[] bytes)
        { using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        static byte[] Pixels(Bitmap image)
        {
            byte[] bytes = new byte[image.Width * image.Height * 4];
            BitmapData data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try { for (int y = 0; y < image.Height; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bytes, y * image.Width * 4, image.Width * 4); }
            finally { image.UnlockBits(data); }
            return bytes;
        }
        static int Different(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) throw new InvalidOperationException("Compared raster sizes differ.");
            int count = 0;
            for (int i = 0; i < a.Length; i += 4)
                if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2] || a[i + 3] != b[i + 3]) count++;
            return count;
        }
        static Dictionary<string, Bitmap> Images(ArtDemoPack pack)
        { return (Dictionary<string, Bitmap>)typeof(ArtDemoPack).GetField("images", Hidden).GetValue(pack); }
        static Bitmap WithoutWeapon(ArtDemoPack pack, string asset, Func<Bitmap> draw)
        {
            Dictionary<string, Bitmap> images = Images(pack); Bitmap original = images[asset];
            using (Bitmap transparent = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppPArgb))
            {
                try { images[asset] = transparent; return draw(); }
                finally { images[asset] = original; }
            }
        }
        static Bitmap Projectile(ArtDemoPack pack, string id, int size, double progress)
        {
            Bitmap frame = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(frame))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    pack.DrawProjectile(g, id, new RectangleF(0, 0, size, size), progress * .5, progress);
                }
                return frame;
            }
            catch { frame.Dispose(); throw; }
        }
        static Bitmap Icon(ArtDemoPack pack, string id)
        {
            Bitmap frame = new Bitmap(128, 128, PixelFormat.Format32bppPArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(frame))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    pack.DrawWeaponIcon(g, id, new RectangleF(8, 8, 112, 112));
                }
                return frame;
            }
            catch { frame.Dispose(); throw; }
        }
        static Dictionary<string, string> Assets(string art)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(art, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
                result.Add(path.Substring(art.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/'), Hash(File.ReadAllBytes(path)));
            return result;
        }
        static FrameResult Record(string key, string kind, Choice choice, int size, double progress, Bitmap original, Bitmap without, Bitmap restored)
        {
            byte[] pixels = Pixels(original), empty = Pixels(without), again = Pixels(restored);
            int visible = 0, transparent = 0, translucent = 0;
            for (int i = 3; i < pixels.Length; i += 4)
            { if (pixels[i] == 0) transparent++; else { visible++; if (pixels[i] < 255) translucent++; } }
            var frame = new FrameResult { key = key, kind = kind, input = choice.Id, mappedAsset = choice.Asset, melee = choice.Melee,
                size = size, progress = progress, visiblePixels = visible, transparentPixels = transparent, translucentPixels = translucent,
                textureDependentPixels = Different(pixels, empty), originalRawSha256 = Hash(pixels), withoutWeaponRawSha256 = Hash(empty), restoredRawSha256 = Hash(again) };
            frames.Add(frame);
            Check(frame.originalRawSha256 == frame.restoredRawSha256, key + ": restoring the same cached weapon restores every exact pixel");
            return frame;
        }
        static void Exercise(string assets, string art, string output, Evidence baseline)
        {
            Dictionary<string, FrameResult> old = baseline == null ? null : baseline.frames.ToDictionary(f => f.key, StringComparer.Ordinal);
            using (ArtDemoPack pack = new ArtDemoPack(art))
            using (AdventureRenderer renderer = new AdventureRenderer(assets, art))
            {
                ArtDemoPack actorPack = (ArtDemoPack)typeof(AdventureRenderer).GetField("artDemo", Hidden).GetValue(renderer);
                foreach (Choice choice in choices)
                {
                    Check(ArtDemoPack.WeaponAsset(choice.Id) == choice.Asset, choice.Name + ": existing null/empty/alias/Trim mapping remains usable");
                    foreach (int size in new[] { 72, 144, 216 }) foreach (double progress in new[] { 0d, .18d, .5d, .9d })
                    {
                        string key = "projectile/" + choice.Name + "/" + size + "/" + progress.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                        using (Bitmap original = Projectile(pack, choice.Id, size, progress))
                        using (Bitmap without = WithoutWeapon(pack, choice.Asset, () => Projectile(pack, choice.Id, size, progress)))
                        using (Bitmap restored = Projectile(pack, choice.Id, size, progress))
                        {
                            FrameResult frame = Record(key, "projectile", choice, size, progress, original, without, restored);
                            Check(frame.visiblePixels > 0 && frame.transparentPixels > 0 && frame.translucentPixels > 0,
                                key + ": existing attack arc remains visible with true transparent and translucent pixels");
                            if (choice.Melee) Check(frame.textureDependentPixels == 0, key + ": melee effect has no second independently rendered physical weapon");
                            else Check(frame.textureDependentPixels > 0, key + ": ranged effect still uses its original physical projectile illustration");
                            if (old != null)
                            {
                                FrameResult before;
                                Check(old.TryGetValue(key, out before), key + ": frozen old-source baseline case exists");
                                if (before != null)
                                {
                                    Check(frame.withoutWeaponRawSha256 == before.withoutWeaponRawSha256, key + ": original arc-only raster is byte-for-byte unchanged from old sources");
                                    if (!choice.Melee) Check(frame.originalRawSha256 == before.originalRawSha256, key + ": every ranged projectile pixel equals the frozen old-source result");
                                }
                            }
                            if (size == 144 && progress == .18 && (choice.Name == "spear" || choice.Name == "greatsword" || choice.Name == "laser"))
                            { original.Save(Path.Combine(output, choice.Name + "-projectile-original.png"), ImageFormat.Png); without.Save(Path.Combine(output, choice.Name + "-projectile-without-texture.png"), ImageFormat.Png); }
                        }
                    }
                    foreach (string phase in new[] { "idle", "attack" }) foreach (bool right in new[] { true, false })
                    {
                        string key = "hero/" + choice.Name + "/" + phase + "/" + right;
                        var state = new AdventureRenderState { Kind = "Hero", HeroStage = choice.Asset == "weapon-zhuji" ? "zhuji" : "mortal", Phase = phase,
                            Size = new Size(220, 240), WeaponId = choice.Id, Time = .31, Cycle = .14, AttackProgress = phase == "attack" ? .5 : 0, FacingRight = right,
                            HealthRatio = 1, Label = "", ActionHint = "", Opacity = 1 };
                        using (Bitmap original = renderer.Render(state))
                        using (Bitmap without = WithoutWeapon(actorPack, choice.Asset, () => renderer.Render(state)))
                        using (Bitmap restored = renderer.Render(state))
                        {
                            FrameResult frame = Record(key, "hero", choice, 220, state.AttackProgress, original, without, restored);
                            Check(frame.textureDependentPixels > 20, key + ": full production hero still visibly holds the original illustrated weapon");
                            if (old != null && old.ContainsKey(key)) Check(frame.originalRawSha256 == old[key].originalRawSha256, key + ": hero frame retains every old-source pixel");
                            if (choice.Name == "spear" && phase == "attack" && right)
                            { original.Save(Path.Combine(output, "spear-held-original.png"), ImageFormat.Png); without.Save(Path.Combine(output, "spear-held-without-texture.png"), ImageFormat.Png); }
                        }
                    }
                    using (Bitmap original = Icon(pack, choice.Id))
                    using (Bitmap without = WithoutWeapon(pack, choice.Asset, () => Icon(pack, choice.Id)))
                    using (Bitmap restored = Icon(pack, choice.Id))
                    {
                        string key = "icon/" + choice.Name;
                        FrameResult frame = Record(key, "icon", choice, 128, 0, original, without, restored);
                        Check(frame.textureDependentPixels > 20, key + ": inventory weapon icon retains the real original illustration");
                        if (old != null && old.ContainsKey(key)) Check(frame.originalRawSha256 == old[key].originalRawSha256, key + ": inventory icon retains every old-source pixel");
                    }
                }
            }
        }
        [STAThread] static int Main(string[] args)
        {
            if (args.Length != 3 && args.Length != 4) { Console.Error.WriteLine("Usage: assetsRoot evidenceRoot artRoot [oldSourceReport]"); return 2; }
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            string output = Path.GetFullPath(args[1]), art = Path.GetFullPath(args[2]), error = null;
            Directory.CreateDirectory(output); Evidence baseline = null;
            var hashes = Assets(art); string baselineHash = null;
            try
            {
                if (args.Length == 4)
                {
                    byte[] source = File.ReadAllBytes(args[3]); baselineHash = Hash(source);
                    baseline = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<Evidence>(System.Text.Encoding.UTF8.GetString(source).TrimStart('\ufeff'));
                    Check(baseline != null && baseline.frames != null && baseline.assetHashes != null && baseline.assetHashes.Count == hashes.Count
                        && hashes.All(p => baseline.assetHashes.ContainsKey(p.Key) && baseline.assetHashes[p.Key] == p.Value), "old-source raster baseline uses exactly the same authored assets");
                    if (baseline == null || baseline.frames == null) throw new InvalidDataException("Frozen source baseline is missing raster records.");
                }
                Exercise(Path.GetFullPath(args[0]), art, output, baseline);
            }
            catch (Exception failure) { error = failure.ToString(); failures++; assertions.Add(new CheckResult { passed = false, detail = "fixture exception: " + error }); Console.WriteLine("FAIL fixture exception: " + error); }
            var report = new Evidence { passed = failures == 0, checks = assertions.Count, failures = failures, seconds = clock.Elapsed.TotalSeconds,
                error = error, baselineProvided = baseline != null, baselineReportSha256 = baselineHash, assetHashes = hashes, frames = frames, assertions = assertions,
                scope = "Pure in-memory real ArtDemoPack and AdventureRenderer pixels. Same-size transparent cache substitution only, restored and disposed on every case. Existing null/empty/valid aliases/Trim inputs; no new invalid-ID contract. No HWND, GUI, input, controller, archive, process census, maintenance, clock advance, HP edit or native-compositor/100-sample proof. Remote baseline comparison is claimed only when baselineProvided=true." };
            File.WriteAllText(Path.Combine(output, "melee-effects.json"), new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Serialize(report), new System.Text.UTF8Encoding(true));
            Console.WriteLine("Melee effects: " + assertions.Count + " checks, " + failures + " failures; " + clock.Elapsed.TotalSeconds.ToString("F3") + " seconds; baseline=" + (baseline != null));
            return failures == 0 ? 0 : 1;
        }
    }
}
