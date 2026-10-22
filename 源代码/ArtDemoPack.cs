using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace NetBunny
{
    // Opt-in xianxia asset pack. Heroes use independently articulated source-pixel
    // rigs; old Assets and the original renderer stay untouched.
    internal sealed class ArtDemoPack : IDisposable
    {
        internal const string EnvironmentVariable = "DESK_TO_GOD_ART_DEMO_ASSETS";
        internal const string MappingDescription = "skeleton=monster-guardian; slime=monster-slime; imp=monster-imp; bat=monster-bat; mushroom=monster-mushroom; boss/elite=elite-guardian; elite-mushroom, mirror-shard, mirror-wraith and mirror-boss use dedicated illustrations. Weapons: spear=weapon-mortal; greatsword=weapon-zhuji; laser=weapon-qishi; wave=weapon-wave; boomerang=weapon-moonring; blackhole=weapon-array; automaton=weapon-talisman. Hero identity follows cultivation stage independently.";
        internal static readonly string[] RequiredAssets = {
            "hero-mortal", "hero-qishi", "hero-zhuji", "weapon-mortal", "weapon-qishi", "weapon-zhuji",
            "weapon-wave", "weapon-moonring", "weapon-array", "weapon-talisman",
            "monster-guardian", "monster-slime", "monster-imp", "monster-bat", "monster-mushroom", "elite-guardian", "elite-mushroom", "mirror-shard", "mirror-wraith", "mirror-boss", "landmark-dongtian",
            "item-spirit-stone", "item-pill", "item-material", "item-scroll", "item-marrow"
        };
        readonly Dictionary<string, Bitmap> images = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> itemKeys = new List<string>();
        readonly Dictionary<string, ArtDemoHeroRig> heroRigs = new Dictionary<string, ArtDemoHeroRig>();
        bool disposed;
        internal readonly string DirectoryPath;

        internal static ArtDemoPack LoadFromEnvironment()
        {
            string path = Environment.GetEnvironmentVariable(EnvironmentVariable);
            return String.IsNullOrWhiteSpace(path) ? null : new ArtDemoPack(path);
        }
        internal ArtDemoPack(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                throw new InvalidDataException("美术 Demo 目录不存在：" + directory);
            DirectoryPath = Path.GetFullPath(directory);
            try
            {
                foreach (string key in RequiredAssets)
                {
                    string path = Path.Combine(DirectoryPath, key + ".png");
                    if (!File.Exists(path)) throw new InvalidDataException("美术 Demo 缺少 " + key + ".png；不会混用旧版角色。目录：" + DirectoryPath);
                    images.Add(key, LoadIllustration(path));
                }
                string[] items = Directory.GetFiles(DirectoryPath, "item-*.png"); Array.Sort(items, StringComparer.OrdinalIgnoreCase);
                foreach (string path in items)
                {
                    string key = Path.GetFileNameWithoutExtension(path); if (!images.ContainsKey(key)) images.Add(key, LoadIllustration(path)); itemKeys.Add(key);
                }
                if (itemKeys.Count == 0) throw new InvalidDataException("美术 Demo 至少需要一张 item-*.png 道具图。");
                foreach (string stage in new[] { "mortal", "qishi", "zhuji" }) heroRigs.Add(stage, new ArtDemoHeroRig(DirectoryPath, stage));
                Trace.WriteLine("Art demo illustration mapping: " + MappingDescription);
            }
            catch { Dispose(); throw; }
        }
        internal static string StageFromRealm(string realm)
        {
            string value = (realm ?? "").Trim().ToLowerInvariant();
            if (value == "mortal" || value.Contains("凡人") || value.Length == 0) return "mortal";
            if (value == "qi" || value == "qishi" || value.Contains("炼气") || value.Contains("练气")) return "qishi";
            return "zhuji";
        }
        internal static string WeaponStage(string id)
        {
            switch ((id ?? "spear").ToLowerInvariant())
            {
                case "mortal": return "mortal";
                case "qishi": return "qishi";
                case "zhuji": return "zhuji";
                case "spear": return "mortal";
                case "laser": case "wave": case "boomerang": case "moonring": return "qishi";
                default: return "zhuji";
            }
        }
        internal static string WeaponAsset(string id)
        {
            switch ((id ?? "spear").Trim().ToLowerInvariant())
            {
                case "": case "spear": case "mortal": return "weapon-mortal";
                case "greatsword": case "zhuji": return "weapon-zhuji";
                case "laser": case "qishi": return "weapon-qishi";
                case "wave": return "weapon-wave";
                case "boomerang": case "moonring": return "weapon-moonring";
                case "blackhole": case "array": return "weapon-array";
                case "automaton": case "talisman": return "weapon-talisman";
                default: throw new InvalidDataException("美术 Demo 尚未映射武器 ID：" + id);
            }
        }
        internal static string MonsterAsset(string kind, bool boss)
        {
            string id = (kind ?? "skeleton").ToLowerInvariant();
            if (id == "mirror-boss") return id;
            if (id == "mirror-shard" || id == "mirror-wraith" || id == "elite-mushroom" || id == "elite-guardian") return id;
            if (id.StartsWith("mirror-", StringComparison.Ordinal)) return StageFromRealm(id.Substring(7)) == "mortal" ? "mirror-shard" : "mirror-wraith";
            if (boss || id == "boss" || id == "elite") return "elite-guardian";
            switch (id)
            {
                case "skeleton": return "monster-guardian";
                case "slime": return "monster-slime";
                case "imp": return "monster-imp";
                case "bat": return "monster-bat";
                case "mushroom": return "monster-mushroom";
                default: throw new InvalidDataException("美术 Demo 尚未映射怪物 ID：" + id);
            }
        }
        static Bitmap LoadIllustration(string path)
        {
            // Remove only near-transparent generation noise before finding the crop.
            // Alpha 13..254 is real authored edge detail and is kept as-is.
            using (Image source = Image.FromFile(path))
            using (Bitmap rgba = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb))
            {
                if (source.Width > 8192 || source.Height > 8192) throw new InvalidDataException("美术 Demo 图片过大：" + path);
                using (Graphics g = Graphics.FromImage(rgba)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(source, 0, 0); }
                BitmapData data = rgba.LockBits(new Rectangle(0, 0, rgba.Width, rgba.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                int left = rgba.Width, top = rgba.Height, right = -1, bottom = -1, transparent = 0;
                try
                {
                    byte[] row = new byte[rgba.Width * 4];
                    for (int y = 0; y < rgba.Height; y++)
                    {
                        IntPtr address = IntPtr.Add(data.Scan0, y * data.Stride); Marshal.Copy(address, row, 0, row.Length);
                        for (int x = 0; x < rgba.Width; x++)
                        {
                            int i = x * 4;
                            if (row[i + 3] <= 12) { row[i] = row[i + 1] = row[i + 2] = row[i + 3] = 0; transparent++; }
                            else { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
                        }
                        Marshal.Copy(row, 0, address, row.Length);
                    }
                }
                finally { rgba.UnlockBits(data); }
                if (right < left || bottom < top || transparent == 0) throw new InvalidDataException("美术 Demo 图片需要真实透明背景与可见内容：" + path);
                Rectangle crop = Rectangle.FromLTRB(Math.Max(0, left - 2), Math.Max(0, top - 2), Math.Min(rgba.Width, right + 3), Math.Min(rgba.Height, bottom + 3));
                float scale = Math.Min(1, 768f / Math.Max(crop.Width, crop.Height));
                Bitmap result = new Bitmap(Math.Max(1, (int)Math.Ceiling(crop.Width * scale)), Math.Max(1, (int)Math.Ceiling(crop.Height * scale)), PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(result))
                {
                    g.CompositingMode = CompositingMode.SourceCopy; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(rgba, new Rectangle(0, 0, result.Width, result.Height), crop, GraphicsUnit.Pixel);
                }
                return result;
            }
        }
        internal static RectangleF Fit(Size source, RectangleF box)
        {
            float scale = Math.Min(box.Width / source.Width, box.Height / source.Height);
            float width = source.Width * scale, height = source.Height * scale;
            return new RectangleF(box.X + (box.Width - width) / 2, box.Bottom - height, width, height);
        }
        internal static RectangleF HeldWeaponBounds(Size source, PointF hand)
        { return HeldWeaponBounds(source, hand, "spear"); }
        internal static RectangleF HeldWeaponBounds(Size source, PointF hand, string weaponId)
        {
            float maxWidth = 47, maxHeight = 104;
            switch (WeaponAsset(weaponId))
            {
                case "weapon-wave": maxWidth = 54; maxHeight = 86; break;
                case "weapon-moonring": maxWidth = 58; maxHeight = 64; break;
                case "weapon-array": maxWidth = 58; maxHeight = 58; break;
                case "weapon-talisman": maxWidth = 43; maxHeight = 70; break;
            }
            float scale = Math.Min(maxWidth / source.Width, maxHeight / source.Height);
            float width = source.Width * scale, height = source.Height * scale;
            // The upright illustration's grip is on the lower hilt, not its centre.
            // This same point is both the placement anchor and the rotation pivot.
            return new RectangleF(hand.X - width * .5f, hand.Y - height * .85f, width, height);
        }
        void Draw(Graphics g, string key, RectangleF box, float opacity, float flash)
        {
            if (disposed) throw new ObjectDisposedException("ArtDemoPack");
            Bitmap image; if (!images.TryGetValue(key, out image)) throw new InvalidDataException("美术 Demo 未加载素材：" + key);
            RectangleF fit = Fit(image.Size, box);
            DrawImage(g, image, fit, new RectangleF(0, 0, image.Width, image.Height), opacity, flash);
        }
        static void DrawImage(Graphics g, Bitmap image, RectangleF target, RectangleF source, float opacity, float flash)
        {
            if (opacity <= 0 || target.Width <= 0 || target.Height <= 0) return;
            using (ImageAttributes attributes = new ImageAttributes())
            {
                float light = Math.Max(0, Math.Min(.65f, flash));
                ColorMatrix matrix = new ColorMatrix(); matrix.Matrix00 = matrix.Matrix11 = matrix.Matrix22 = 1 - light;
                matrix.Matrix40 = matrix.Matrix41 = matrix.Matrix42 = light; matrix.Matrix33 = Math.Max(0, Math.Min(1, opacity)); attributes.SetColorMatrix(matrix);
                PointF[] points = { new PointF(target.Left, target.Top), new PointF(target.Right, target.Top), new PointF(target.Left, target.Bottom) };
                g.DrawImage(image, points, source, GraphicsUnit.Pixel, attributes);
            }
        }
        static double Clamp(double value) { return Double.IsNaN(value) ? 0 : Math.Max(0, Math.Min(1, value)); }
        static void Shadow(Graphics g, float width, float alpha)
        { using (Brush b = new SolidBrush(Color.FromArgb((int)Math.Max(0, Math.Min(70, alpha)), 28, 43, 44))) g.FillEllipse(b, 110 - width / 2, 183, width, 10); }
        static void Ring(Graphics g, double time, Color color, float alpha)
        {
            using (Pen p = new Pen(Color.FromArgb((int)(Math.Max(0, Math.Min(1, alpha)) * 150), color), 2))
            { g.DrawEllipse(p, 58, 174, 104, 17); g.DrawArc(p, 47, 169, 126, 26, (float)(time * 44), 126); }
        }
        internal void DrawHero(Graphics g, AdventureRenderState s)
        {
            string phase=(s.Phase??"idle").ToLowerInvariant(),stage=StageFromRealm(s.HeroStage);
            bool attack=phase=="attack"||s.AttackProgress>.03,rest=phase=="grave"||phase=="dead"||phase=="tombstone",revive=phase=="revive"||phase=="reviving";
            Shadow(g,91,rest?30:48);
            if(stage!="mortal"||rest||revive||s.GlowProgress>0) Ring(g,s.Time,stage=="zhuji"?Color.FromArgb(183,211,255):Color.FromArgb(126,216,196),rest?.55f:.75f);
            heroRigs[stage].Draw(g,s,images[WeaponAsset(s.WeaponId)]);
            if(attack)
            {
                // The held weapon is already attached to the forearm. A melee
                // swing adds only a small arc, never a second floating sword.
                GraphicsState arc=g.Save();if(!s.FacingRight){g.TranslateTransform(220,0);g.ScaleTransform(-1,1);}
                Color color=stage=="mortal"?Color.FromArgb(225,201,145):stage=="qishi"?Color.FromArgb(135,215,208):Color.FromArgb(184,204,239);
                float strength=(float)Math.Sin(Clamp(s.AttackProgress)*Math.PI);
                using(Pen pen=new Pen(Color.FromArgb((int)(145*strength),color),2.2f)) g.DrawArc(pen,120,69,84,100,(float)(-60+s.AttackProgress*95),64);
                g.Restore(arc);
            }
        }
        internal void DrawMonster(Graphics g, AdventureRenderState s, bool boss, bool evil)
        {
            string phase = (s.Phase ?? "idle").ToLowerInvariant(); bool shatter = phase == "shatter" || phase == "defeated" || phase == "dead" || phase == "vanish";
            double death = shatter ? Clamp(s.DeathProgress) : 0; if (shatter && death >= 1) return;
            string key = evil ? "mirror-boss" : MonsterAsset(s.MonsterKind, boss);
            Shadow(g, boss || evil ? 115 : 85, (float)(44 * (1 - death)));
            GraphicsState save = g.Save();
            if (!s.FacingRight) { g.TranslateTransform(220, 0); g.ScaleTransform(-1, 1); }
            float walk = (float)Math.Sin(s.Time * 7), bob = (float)(Math.Sin(s.Time * 4.5) * 1.6);
            g.TranslateTransform(110, 180 + bob); g.RotateTransform(phase == "attack" ? (float)(Math.Sin(s.Time * 18) * 7) : walk * (Math.Abs(s.MoveSpeed) > 1 ? 2 : .5f)); g.TranslateTransform(-110, -180);
            if (!shatter)
            {
                if (s.HitFlash > 0) g.TranslateTransform((float)(-Math.Sin(s.Time * 41) * Math.Min(5, s.HitFlash * 5)), 0);
                Draw(g, key, new RectangleF(boss || evil ? 23 : 37, 45, boss || evil ? 174 : 146, 145), 1, (float)Clamp(s.HitFlash) * .6f);
            }
            else
            {
                Bitmap image = images[key]; RectangleF fit = Fit(image.Size, new RectangleF(boss || evil ? 23 : 37, 45, boss || evil ? 174 : 146, 145));
                for (int i = 0; i < 8; i++)
                {
                    float y0 = image.Height * i / 8f, y1 = image.Height * (i + 1) / 8f;
                    RectangleF target = new RectangleF(fit.X + (float)(Math.Cos(i * 2.4) * death * 49), fit.Y + fit.Height * i / 8 + (float)(Math.Sin(i * 2.4) * death * 23 + death * death * 18), fit.Width, fit.Height / 8 + .5f);
                    DrawImage(g, image, target, new RectangleF(0, y0, image.Width, y1 - y0), (float)(1 - death), 0);
                }
            }
            g.Restore(save);
        }
        internal void DrawLandmark(Graphics g, AdventureRenderState s)
        { Shadow(g, 163, 45); Draw(g, "landmark-dongtian", new RectangleF(12, 2, 196, 193), 1, 0); }
        internal void DrawLoot(Graphics g, AdventureRenderState s)
        {
            Ring(g, s.Time, Color.FromArgb(213, 193, 123), .8f);
            RectangleF box = new RectangleF(62, 47 + (float)Math.Sin(s.Time * 3) * 5, 96, 123);
            if (Array.IndexOf(RasterActorRenderer.WeaponIds, s.WeaponId ?? "spear") >= 0) DrawWeaponIcon(g, s.WeaponId, box);
            else DrawItemIcon(g, s.WeaponId, box);
        }
        internal bool DrawWeaponIcon(Graphics g, string id, RectangleF box)
        { Draw(g, WeaponAsset(id), box, 1, 0); return true; }
        internal static string ItemAsset(string id)
        {
            string key = (id ?? "").ToLowerInvariant();
            if (key.Contains("lingsui") || key.Contains("marrow") || key.Contains("灵髓")) return "item-marrow";
            if (key.Contains("scroll") || key.Contains("卷")) return "item-scroll";
            if (key.Contains("pill") || key.Contains("丹")) return "item-pill";
            if (key.Contains("stone") || key.Contains("灵石")) return "item-spirit-stone";
            return "item-material";
        }
        internal bool DrawItemIcon(Graphics g, string id, RectangleF box)
        { Draw(g, ItemAsset(id), box, 1, 0); return true; }
        internal bool DrawProjectile(Graphics g, string weaponId, RectangleF box, double time, double progress)
        {
            string stage = WeaponStage(weaponId); Color color = stage == "mortal" ? Color.FromArgb(236, 203, 126) : stage == "qishi" ? Color.FromArgb(132, 225, 211) : Color.FromArgb(187, 201, 255);
            float fade = (float)Math.Min(1, Math.Max(0, (1 - progress) * 4)), cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2;
            GraphicsState save = g.Save(); g.TranslateTransform(cx, cy); g.ScaleTransform(box.Width / 192f, box.Height / 192f);
            float radius = (float)(25 + Math.Sin(Clamp(progress) * Math.PI) * 16);
            for (int i = 3; i >= 0; i--)
            {
                using (Pen pen = new Pen(Color.FromArgb((int)((160 - i * 32) * fade), color), 2 + i * 2))
                    g.DrawArc(pen, -radius - i * 6, -radius - i * 6, (radius + i * 6) * 2, (radius + i * 6) * 2, (float)(-70 + progress * 160 + i * 19), 118);
            }
            // Melee steel stays in the hero's hand; its hit effect is the arc.
            // Only ranged attacks carry a second illustrated object in flight.
            string asset = WeaponAsset(weaponId);
            if (asset != "weapon-mortal" && asset != "weapon-zhuji")
            {
                g.RotateTransform((float)(weaponId == "boomerang" ? progress * 720 : -24 + progress * 45));
                Draw(g, asset, new RectangleF(-22, -51, 44, 94), fade, 0);
            }
            g.Restore(save); return true;
        }
        internal void Validate(Action<bool, string> check)
        {
            foreach (string key in RequiredAssets)
            {
                Bitmap image = images[key]; check(image.Width > 0 && image.Height > 0 && image.Width <= 768 && image.Height <= 768, key + " loaded into bounded illustration cache");
                check(image.PixelFormat == PixelFormat.Format32bppPArgb, key + " has premultiplied alpha cache");
            }
            check(itemKeys.Count > 0, "new item illustrations available");
            HashSet<string> weapons = new HashSet<string>();
            foreach (string id in RasterActorRenderer.WeaponIds)
            {
                string key = WeaponAsset(id); weapons.Add(key);
                check(images.ContainsKey(key), "mapped gameplay weapon " + id + " uses " + key);
                RectangleF held = HeldWeaponBounds(images[key].Size, new PointF(143, 134), id);
                check(held.Width <= 58.001f && held.Height <= 104.001f, id + " has bounded hand-held composition");
            }
            check(weapons.Count == RasterActorRenderer.WeaponIds.Length, "seven gameplay weapons have seven distinct illustrations");
            foreach (ArtDemoHeroRig rig in heroRigs.Values) rig.Validate(check);
            foreach (string id in RasterActorRenderer.MonsterKinds) check(images.ContainsKey(MonsterAsset(id, id == "boss")), "mapped gameplay monster " + id);
            HashSet<string> common = new HashSet<string>(); foreach (string id in new[] { "skeleton", "slime", "imp", "bat", "mushroom" }) common.Add(MonsterAsset(id, false));
            check(common.Count == 5, "five gameplay common IDs have five explicit new illustrations");
            check(ItemAsset("shuiyue-lingsui") == "item-marrow" && ItemAsset("jingxin-scroll") == "item-scroll" && ItemAsset("聚气丹") == "item-pill" && ItemAsset("灵石") == "item-spirit-stone" && ItemAsset("jinghua-shuiyue") == "item-material", "five item categories map by gameplay meaning");
            check(Math.Abs(Fit(new Size(100, 200), new RectangleF(0, 0, 220, 240)).Width - 120) < .001, "portrait illustration fit preserves aspect ratio");
            foreach (string stage in new[] { "mortal", "qishi", "zhuji" })
            {
                PointF hand = new PointF(143, 134); RectangleF held = HeldWeaponBounds(images["weapon-" + stage].Size, hand);
                check(Math.Abs(held.X + held.Width * .5f - hand.X) < .001 && Math.Abs(held.Y + held.Height * .85f - hand.Y) < .001, stage + " hilt anchor and rotation pivot meet the same hand");
            }
        }
        public void Dispose()
        { if (disposed) return; disposed = true; foreach (Bitmap image in images.Values) image.Dispose(); images.Clear(); itemKeys.Clear(); foreach (ArtDemoHeroRig rig in heroRigs.Values) rig.Dispose(); heroRigs.Clear(); }
    }
}
