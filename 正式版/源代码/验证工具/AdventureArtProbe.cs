using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
namespace NetBunny
{
    internal static class AdventureArtProbe
    {
        private static void Main(string[] args)
        {
          try {
            string source = args[0];
            Console.WriteLine(AdventureRenderer.SelfTest());
            KnightBrand.ExportIcon(Path.Combine(source, "knight.ico"));
            AdventureRenderer.ExportPreview(Path.Combine(source, "Assets"), Path.Combine(source, "adventure-art-preview.png"));
            AdventureRenderer.ExportAnimationFrames(Path.Combine(source, "Assets"), Path.Combine(source, "验证工具", "Run60"));
            ExportGif(Path.Combine(source, "验证工具", "Run60"), Path.Combine(source, "knight-run-smooth.gif"));
            Console.WriteLine("Saved knight.ico, adventure-art-preview.png and 60 continuous running poses.");
          } catch(Exception ex) { File.WriteAllText(Path.Combine(args[0], "art-test-error.txt"), ex.GetType().FullName + "\n" + ex.Message + "\n" + ex.StackTrace); Environment.ExitCode=1; }
        }
        private static void ExportGif(string folder, string target)
        {
            // GIF container assembly for original code-rendered frames; optional AI illustration assets are not transformed.
            using (BinaryWriter output = new BinaryWriter(File.Create(target)))
            {
                output.Write(Encoding.ASCII.GetBytes("GIF89a")); output.Write((ushort)220); output.Write((ushort)220); output.Write((byte)0x70); output.Write((byte)0); output.Write((byte)0);
                output.Write(new byte[] { 0x21, 0xff, 11 }); output.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0")); output.Write(new byte[] { 3, 1, 0, 0, 0 });
                for (int i = 0; i < 60; i++)
                {
                    byte[] bytes;
                    using (Bitmap frame = new Bitmap(220, 220)) using (Graphics g = Graphics.FromImage(frame)) using (Image source = Image.FromFile(Path.Combine(folder, "run-" + i.ToString("D3") + ".png"))) using (MemoryStream stream = new MemoryStream())
                    { g.Clear(Color.FromArgb(21, 28, 46)); g.DrawImageUnscaled(source, 0, 0); frame.Save(stream, ImageFormat.Gif); bytes = stream.ToArray(); }
                    int paletteBits = bytes[10] & 7, paletteLength = (bytes[10] & 128) != 0 ? 3 * (1 << (paletteBits + 1)) : 0, cursor = 13 + paletteLength;
                    while (bytes[cursor] == 0x21) { cursor += 2; while (bytes[cursor] != 0) cursor += bytes[cursor] + 1; cursor++; }
                    if (bytes[cursor] != 0x2c) throw new InvalidDataException("GIF image descriptor missing");
                    int ownPacked = bytes[cursor + 9], ownPalette = (ownPacked & 128) != 0 ? 3 * (1 << ((ownPacked & 7) + 1)) : 0;
                    output.Write(new byte[] { 0x21, 0xf9, 4, 4, 2, 0, 0, 0 });
                    output.Write(bytes, cursor, 9); output.Write((byte)((ownPacked & 64) | 128 | (ownPalette > 0 ? ownPacked & 7 : paletteBits)));
                    if (ownPalette > 0) output.Write(bytes, cursor + 10, ownPalette); else output.Write(bytes, 13, paletteLength);
                    int start = cursor + 10 + ownPalette; cursor = start + 1; while (bytes[cursor] != 0) cursor += bytes[cursor] + 1; cursor++;
                    output.Write(bytes, start, cursor - start);
                }
                output.Write((byte)0x3b);
            }
        }
    }
}
