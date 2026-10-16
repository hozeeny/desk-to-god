using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace NetBunny
{
    // Continuous skeletal animation of hand-painted raster parts. None of the actor states
    // advance through a finite sprite frame list, so movement remains smooth at any speed.
    internal sealed class RasterActorRenderer : IDisposable
    {
        internal static readonly string[] ThemeCodes = { "OTHER", "CN", "JP", "US", "TW", "HK" };
        internal static readonly string[] MonsterKinds = { "skeleton", "slime", "imp", "bat", "mushroom", "boss" };
        // Authored PNG silhouettes differ: skull/bat look right, the other four look left.
        // FacingRight describes world movement, never the unnormalised atlas orientation.
        private static readonly bool[] MonsterSourceFacesRight = { true, false, false, true, false, false };
        internal static readonly string[] WeaponIds = { "spear", "greatsword", "wave", "laser", "boomerang", "blackhole", "automaton" };
        private readonly Dictionary<string, RasterAtlas> rigs = new Dictionary<string, RasterAtlas>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PartAnchors[]> anatomy = new Dictionary<string, PartAnchors[]>(StringComparer.OrdinalIgnoreCase);
        private sealed class PartAnchors { internal PointF Start, End, Ankle; internal float Sole, FootCut; }
        internal readonly RasterAtlas Monsters, Weapons, Grave;
        internal RasterActorRenderer(string directory)
        {
            foreach (string theme in ThemeCodes)
            {
                RasterAtlas rig = RasterAtlas.TryLoad(directory, "rig-" + theme + ".png", 4, 3);
                if (rig != null) { rigs[theme] = rig; anatomy[theme] = BuildAnchors(rig); }
            }
            Monsters = RasterAtlas.TryLoad(directory, "monsters-v5.png", 3, 2);
            Weapons = RasterAtlas.TryLoad(directory, "weapons-v5.png", 4, 2);
            Grave = RasterAtlas.TryLoad(directory, "grave-v5.png", 2, 2);
        }
        internal RasterAtlas Rig(string theme) { RasterAtlas rig; return rigs.TryGetValue(theme ?? "OTHER", out rig) ? rig : null; }
        internal bool HasRig(string theme) { RasterAtlas rig = Rig(theme); if (rig == null) return false; for (int i = 0; i < 11; i++) if (!rig.HasCell(i)) return false; return true; }
        internal static double NormalizedCycle(AdventureRenderState s)
        {
            double raw = Double.IsNaN(s.Cycle) ? s.Time * (.65 + Math.Min(2.6, Math.Abs(s.MoveSpeed) / 90)) : s.Cycle;
            return raw - Math.Floor(raw);
        }
        internal bool DrawHero(Graphics g, AdventureRenderState s)
        {
            RasterAtlas rig = Rig(s.ThemeCode); if (!HasRig(s.ThemeCode)) return false;
            string phase = (s.Phase ?? "run").ToLowerInvariant();
            bool run = phase == "run" || phase == "running", pant = phase == "pant" || phase == "panting" || phase == "idle" || phase == "rest";
            bool attacking = phase == "attack" || s.AttackProgress > .03, dying = phase == "dying" || phase == "death", revive = phase == "revive" || phase == "reviving";
            double a = NormalizedCycle(s) * Math.PI * 2;
            HeroPose pose = HeroPoseRig.Evaluate(s, WeaponGrip(s.WeaponId)); PartAnchors[] anchors = anatomy[s.ThemeCode ?? "OTHER"];
            GraphicsState original = g.Save();
            if (!s.FacingRight) { g.TranslateTransform(220, 0); g.ScaleTransform(-1, 1); }
            if (revive)
            {
                if (Grave != null) Grave.DrawFit(g, 3, new RectangleF(51, 151, 124, 43));
                g.SetClip(new RectangleF(5, 5, 210, 183));
                double p = Clamp(s.DeathProgress > 0 ? s.DeathProgress : s.PhaseTime / 2.4);
                g.TranslateTransform(0, (float)(70 * (1 - p)));
            }
            if (dying)
            {
                double p = Clamp((s.DeathProgress > 0 ? s.DeathProgress : s.PhaseTime / 3.4) / .7);
                g.TranslateTransform(124, 151); g.RotateTransform((float)(-70 * p)); g.ScaleTransform((float)(1 - .25 * p), (float)(1 - .25 * p)); g.TranslateTransform(-124, -151);
            }
            GraphicsState body = g.Save(); ApplyBody(g, pose);
            // Cape root is attached to the rear shoulder; its free edge follows the gait.
            GraphicsState cape = g.Save(); g.TranslateTransform(101, 109); g.RotateTransform((float)(run ? -7 + Math.Sin(a - .65) * 7 : Math.Sin(s.Time * 2) * 2));
            rig.Draw(g, 10, new RectangleF(run ? -67 : -57, -5, run ? 73 : 63, run ? 62 : 68)); g.Restore(cape);
            Segment(g, rig, 4, anchors[4].Start, anchors[4].End, pose.FarShoulder, pose.FarElbow, null);
            g.Restore(body);
            DrawLeg(g, rig, anchors, pose.FarFoot, true);
            body = g.Save(); ApplyBody(g, pose);
            rig.Draw(g, 1, new RectangleF(85, 103, 55, 53));
            g.Restore(body);
            // The near thigh is in front of the skirt plates, while the far leg remains behind.
            DrawLeg(g, rig, anchors, pose.NearFoot, false);
            body = g.Save(); ApplyBody(g, pose);
            Segment(g, rig, 2, anchors[2].Start, anchors[2].End, pose.NearShoulder, pose.NearElbow, null);
            GraphicsState head = g.Save(); g.TranslateTransform(111, 109); g.RotateTransform((float)(pant ? 8 + Math.Sin(s.Time * 6) * 1.4 : -pose.Lean * .55 + Math.Sin(a) * .65)); g.TranslateTransform(-111, -109);
            Rectangle hb = rig.Bounds[0]; float headHeight = 91, headWidth = Math.Min(118, headHeight * hb.Width / hb.Height);
            rig.Draw(g, 0, new RectangleF(111 - headWidth * .57f, 20, headWidth, headHeight)); g.Restore(head);
            DrawWeapon(g, s.WeaponId, pose.NearHand.X, pose.NearHand.Y, (float)pose.WeaponAngle, s.Time, s.AttackProgress);
            // Both gripping hands and forearms are foreground layers, never buried under the torso.
            Segment(g, rig, 5, anchors[5].Start, anchors[5].End, pose.FarElbow, pose.FarHand, null);
            Segment(g, rig, 3, anchors[3].Start, anchors[3].End, pose.NearElbow, pose.NearHand, null);
            g.Restore(body); g.Restore(original);
            if (revive && Grave != null) Grave.DrawFit(g, 2, new RectangleF(58, 167, 113, 30));
            return true;
        }
        private static void ApplyBody(Graphics g, HeroPose pose)
        {
            g.TranslateTransform((float)(-8 * pose.Attack), 0); g.TranslateTransform(110, (float)(143 + pose.Bob)); g.RotateTransform((float)pose.Lean); g.TranslateTransform(-110, -143);
        }
        private static PartAnchors[] BuildAnchors(RasterAtlas rig)
        {
            PartAnchors[] result = new PartAnchors[12];
            for (int i = 2; i <= 9; i++)
            {
                if (!rig.HasCell(i)) continue; Bitmap cell = rig.CellImage(i);
                PartAnchors p = result[i] = new PartAnchors { Start = BandCenter(cell, .14f), End = BandCenter(cell, i == 3 || i == 5 ? .84f : .85f) };
                if (i == 7 || i == 9)
                {
                    float ankleFraction = Path.GetFileName(rig.PathName).Equals("rig-TW.png", StringComparison.OrdinalIgnoreCase) ? .59f : .68f;
                    p.Start = BandCenter(cell, .12f); p.Ankle = BandCenter(cell, ankleFraction); p.End = p.Ankle; p.FootCut = cell.Height * (ankleFraction - .08f);
                    p.Sole = cell.Height - 1;
                    for (int y = cell.Height - 1; y >= cell.Height * .8; y--) { bool found = false; for (int x = 0; x < cell.Width; x++) if (cell.GetPixel(x, y).A > 80) { found = true; break; } if (found) { p.Sole = y; break; } }
                }
            }
            return result;
        }
        private static PointF BandCenter(Bitmap image, float fraction)
        {
            float yCenter = (image.Height - 1) * fraction; int from = Math.Max(0, (int)yCenter - 3), to = Math.Min(image.Height - 1, (int)yCenter + 3); double sum = 0, weight = 0;
            for (int y = from; y <= to; y++) for (int x = 0; x < image.Width; x++) { int alpha = image.GetPixel(x, y).A; if (alpha < 40) continue; sum += x * alpha; weight += alpha; }
            return new PointF(weight > 0 ? (float)(sum / weight) : image.Width * .5f, yCenter);
        }
        private static void Segment(Graphics g, RasterAtlas rig, int part, PointF sourceFrom, PointF sourceTo, PointF from, PointF to, RectangleF? sourceRegion)
        {
            Bitmap cell = rig.CellImage(part); double sx = sourceTo.X - sourceFrom.X, sy = sourceTo.Y - sourceFrom.Y, dx = to.X - from.X, dy = to.Y - from.Y;
            double length = Math.Sqrt(dx * dx + dy * dy), sourceLength = Math.Sqrt(sx * sx + sy * sy); float scale = (float)(length / Math.Max(1, sourceLength));
            GraphicsState state = g.Save(); g.TranslateTransform(from.X, from.Y); g.RotateTransform((float)((Math.Atan2(dy, dx) - Math.Atan2(sy, sx)) * 180 / Math.PI)); g.ScaleTransform(scale, scale); g.TranslateTransform(-sourceFrom.X, -sourceFrom.Y);
            RectangleF region = sourceRegion ?? new RectangleF(0, 0, cell.Width, cell.Height); g.InterpolationMode = InterpolationMode.Bilinear; g.DrawImage(cell, region, region, GraphicsUnit.Pixel); g.Restore(state);
        }
        private static void DrawLeg(Graphics g, RasterAtlas rig, PartAnchors[] anchors, HeroFootPose pose, bool far)
        {
            int thigh = far ? 8 : 6, shin = far ? 9 : 7; Bitmap cell = rig.CellImage(shin); PartAnchors a = anchors[shin];
            Segment(g, rig, thigh, anchors[thigh].Start, anchors[thigh].End, pose.Hip, pose.Knee, null);
            Segment(g, rig, shin, a.Start, a.Ankle, pose.Knee, pose.Ankle, new RectangleF(0, 0, cell.Width, a.Ankle.Y + cell.Height * .035f));
            // The original shin+boot texture is sampled as two overlapping regions. Soles
            // remain level while planted; torso lean never rotates the contact point.
            float scale = 10 / Math.Max(1, a.Sole - a.Ankle.Y);
            GraphicsState state = g.Save(); g.TranslateTransform(pose.Ankle.X, pose.Ankle.Y); g.RotateTransform((float)pose.FootAngle); g.ScaleTransform(scale, scale); g.TranslateTransform(-a.Ankle.X, -a.Ankle.Y);
            RectangleF foot = new RectangleF(0, a.FootCut, cell.Width, cell.Height - a.FootCut); g.InterpolationMode = InterpolationMode.Bilinear; g.DrawImage(cell, foot, foot, GraphicsUnit.Pixel); g.Restore(state);
        }
        internal HeroWeaponGrip WeaponGrip(string id)
        {
            int index = WeaponIndex(id); float[] widths = { 137, 99, 94, 87, 66, 87, 85 };
            PointF[] main = { new PointF(.34f,.58f), new PointF(.12f,.78f), new PointF(.17f,.82f), new PointF(.26f,.78f), new PointF(.72f,.52f), new PointF(.15f,.82f), new PointF(.26f,.78f) };
            PointF[] support = { new PointF(.54f,.43f), new PointF(.25f,.60f), new PointF(.38f,.62f), new PointF(.55f,.52f), new PointF(.72f,.52f), new PointF(.37f,.64f), new PointF(.58f,.52f) };
            float aspect = Weapons != null && Weapons.HasCell(index) ? Weapons.Bounds[index].Height / (float)Weapons.Bounds[index].Width : .5f;
            return new HeroWeaponGrip { Index = index, Width = widths[index], Height = Math.Min(index == 0 ? 78 : 83, widths[index] * aspect), Main = main[index], Support = support[index], TwoHands = index != 4 };
        }
        internal bool DrawWeapon(Graphics g, string id, float x, float y, float angle, double time, double attack)
        {
            if (Weapons == null) return false; int index = WeaponIndex(id); if (!Weapons.HasCell(index)) return false;
            HeroWeaponGrip grip = WeaponGrip(id); float width = grip.Width, height = grip.Height;
            GraphicsState state = g.Save(); g.TranslateTransform(x, y); g.RotateTransform(angle);
            Weapons.Draw(g, index, new RectangleF(-width * grip.Main.X, -height * grip.Main.Y, width, height)); g.Restore(state); return true;
        }
        internal bool DrawWeaponIcon(Graphics g, string id, RectangleF box) { if (Weapons == null || !Weapons.HasCell(WeaponIndex(id))) return false; Weapons.DrawFit(g, WeaponIndex(id), box); return true; }
        private static int WeaponIndex(string id)
        {
            string key = (id ?? "spear").ToLowerInvariant(); if (key == "shockwave") key = "wave"; if (key == "auto") key = "automaton";
            int index = Array.IndexOf(WeaponIds, key); return index < 0 ? 0 : index;
        }
        internal bool DrawGrave(Graphics g, AdventureRenderState s, double fade)
        {
            if (Grave == null || !Grave.HasCell(0) || !Grave.HasCell(1)) return false;
            Grave.DrawFit(g, 0, new RectangleF(46, 56, 137, 139));
            float ghostY = (float)(17 + Math.Sin(s.Time * 2.5) * 4 - Math.Min(10, s.PhaseTime * 2));
            Grave.DrawFit(g, 1, new RectangleF(78, ghostY, 62, 75)); return true;
        }
        internal bool DrawMonster(Graphics g, AdventureRenderState s, bool boss)
        {
            if (Monsters == null) return false; int index = boss ? 5 : Array.IndexOf(MonsterKinds, (s.MonsterKind ?? "skeleton").ToLowerInvariant()); if (index < 0) index = 0;
            if (!Monsters.HasCell(index)) return false;
            string phase = (s.Phase ?? "walk").ToLowerInvariant(); bool shatter = phase == "defeated" || phase == "shatter" || phase == "dead" || phase == "vanish";
            Rectangle src = Monsters.Bounds[index]; float fit = Math.Min((boss ? 184 : 153) / (float)src.Width, (boss ? 155 : 142) / (float)src.Height), w = src.Width * fit, h = src.Height * fit;
            RectangleF body = new RectangleF(110 - w / 2, 185 - h, w, h);
            GraphicsState original = g.Save(); if (s.FacingRight != MonsterSourceFacesRight[index]) { g.TranslateTransform(220, 0); g.ScaleTransform(-1, 1); }
            double t = s.Time, wave = Math.Sin(t * (boss ? 4 : index == 3 ? 11 : 6)), bounce = 0, sx = 1, sy = 1, rotate = 0;
            if (index == 1) { sx = 1 + Math.Cos(t * 6) * .12; sy = 1 - Math.Cos(t * 6) * .11; bounce = -Math.Max(0, wave) * 13; }
            else if (index == 2) { bounce = -3 - wave * 3; rotate = wave * 3; }
            else if (index == 3) { bounce = -13 - Math.Sin(t * 4) * 9; sx = 1 + wave * .12; sy = 1 - wave * .035; rotate = Math.Sin(t * 4) * 3; }
            else if (index == 4) { bounce = -Math.Max(0, wave) * 8; sx = 1 + Math.Cos(t * 6) * .035; sy = 1 - Math.Cos(t * 6) * .035; rotate = wave * 4; }
            else if (boss) { bounce = -Math.Abs(wave) * 2.5; rotate = wave * 1.2; }
            else { bounce = -Math.Abs(wave) * 3.5; rotate = wave * 3; }
            if (shatter)
            {
                Bitmap fragmentImage = Monsters.CellImage(index); Rectangle fragmentSource = new Rectangle(0, 0, fragmentImage.Width, fragmentImage.Height);
                double p = Clamp(s.DeathProgress);
                using (ImageAttributes attrs = new ImageAttributes())
                {
                    ColorMatrix matrix = new ColorMatrix(); matrix.Matrix33 = (float)(1 - p); attrs.SetColorMatrix(matrix);
                    for (int n = 0; n < 9; n++)
                    {
                        Rectangle fragment = Rectangle.FromLTRB(n % 3 * fragmentSource.Width / 3, n / 3 * fragmentSource.Height / 3, (n % 3 + 1) * fragmentSource.Width / 3, (n / 3 + 1) * fragmentSource.Height / 3);
                        double angle = n * 2.399; float dx = (float)(Math.Cos(angle) * p * 47), dy = (float)(Math.Sin(angle) * p * 35 + p * p * 15);
                        RectangleF target = new RectangleF(body.X + n % 3 * body.Width / 3 + dx, body.Y + n / 3 * body.Height / 3 + dy, body.Width / 3, body.Height / 3);
                        PointF[] points = { new PointF(target.Left, target.Top), new PointF(target.Right, target.Top), new PointF(target.Left, target.Bottom) };
                        g.DrawImage(fragmentImage, points, fragment, GraphicsUnit.Pixel, attrs);
                    }
                }
            }
            else
            {
                g.TranslateTransform(110, (float)(185 + bounce)); g.RotateTransform((float)rotate); g.ScaleTransform((float)sx, (float)sy); g.TranslateTransform(-110, -185);
                Monsters.DrawFade(g, index, body, 1, (float)(s.HitFlash * 2));
            }
            g.Restore(original); return true;
        }
        internal int Validate(Action<bool, string> check)
        {
            int count = 0;
            foreach (string theme in ThemeCodes) { RasterAtlas rig = Rig(theme); check(rig != null, "required raster rig " + theme); count++; if (rig == null) continue; count += ValidateAtlas(rig, 12, check); }
            check(Monsters != null, "required five monster types plus boss atlas"); check(Weapons != null, "required seven weapons atlas"); check(Grave != null, "required grave and ghost atlas"); count += 3;
            if (Monsters != null) count += ValidateAtlas(Monsters, 6, check); if (Weapons != null) count += ValidateAtlas(Weapons, 8, check); if (Grave != null) count += ValidateAtlas(Grave, 4, check); return count;
        }
        private static int ValidateAtlas(RasterAtlas atlas, int count, Action<bool, string> check)
        {
            string name = Path.GetFileName(atlas.PathName); check(File.Exists(Path.ChangeExtension(atlas.PathName, ".rects")), name + " has verified source rectangles"); check(atlas.TransparentRatio > .15 && atlas.TransparentRatio < .98, name + " contains actual transparent background");
            for (int i = 0; i < count; i++) check(atlas.HasCell(i) && atlas.InkPixels[i] > atlas.Cells[i].Width * atlas.Cells[i].Height * .015, name + " has nonempty expected cell " + i); return count + 2;
        }
        private static double Clamp(double x) { return Math.Max(0, Math.Min(1, x)); }
        private static double Lift(double x) { double p = Math.Max(0, x); return p * p; }
        public void Dispose() { foreach (RasterAtlas rig in rigs.Values) rig.Dispose(); rigs.Clear(); if (Monsters != null) Monsters.Dispose(); if (Weapons != null) Weapons.Dispose(); if (Grave != null) Grave.Dispose(); }
    }
}
