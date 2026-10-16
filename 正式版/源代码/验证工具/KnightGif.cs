using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

// Builds an animation from application-authored PNG preview frames.
// Atlas sprites are read by the application renderer; this helper never modifies them.
// GIF89a container reference: https://www.w3.org/Graphics/GIF/spec-gif89a.txt
internal static class KnightGif
{
    private static int SkipBlocks(byte[] bytes, int position)
    {
        while (true)
        {
            if (position >= bytes.Length) throw new InvalidDataException("Unterminated GIF data.");
            int count = bytes[position++];
            if (count == 0) return position;
            position += count;
            if (position > bytes.Length) throw new InvalidDataException("Truncated GIF data.");
        }
    }

    private static void WriteFrame(BinaryWriter writer, byte[] bytes, int frame, int fps)
    {
        if (Encoding.ASCII.GetString(bytes, 0, 3) != "GIF") throw new InvalidDataException("GIF frame expected.");
        int tableSize = (bytes[10] & 128) != 0 ? 3 * (1 << ((bytes[10] & 7) + 1)) : 0;
        int position = 13 + tableSize;
        while (position < bytes.Length && bytes[position] == 0x21)
            position = SkipBlocks(bytes, position + 2);
        if (position + 10 >= bytes.Length || bytes[position] != 0x2c)
            throw new InvalidDataException("GIF frame missing image descriptor.");
        byte[] descriptor = new byte[10];
        Buffer.BlockCopy(bytes, position, descriptor, 0, descriptor.Length);
        position += descriptor.Length;
        byte[] palette;
        if ((descriptor[9] & 128) != 0)
        {
            int localSize = 3 * (1 << ((descriptor[9] & 7) + 1));
            palette = new byte[localSize];
            Buffer.BlockCopy(bytes, position, palette, 0, localSize);
            position += localSize;
        }
        else
        {
            if (tableSize == 0) throw new InvalidDataException("GIF frame missing palette.");
            palette = new byte[tableSize];
            Buffer.BlockCopy(bytes, 13, palette, 0, tableSize);
            descriptor[9] = (byte)((descriptor[9] & 0x78) | 0x80 | (bytes[10] & 7));
        }
        int delay = (int)Math.Round((frame + 1) * 100.0 / fps) - (int)Math.Round(frame * 100.0 / fps);
        writer.Write(new byte[] { 0x21, 0xf9, 4, 4, (byte)delay, (byte)(delay >> 8), 0, 0 });
        writer.Write(descriptor);
        writer.Write(palette);
        int end = SkipBlocks(bytes, position + 1);
        writer.Write(bytes, position, end - position);
    }

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length < 2) throw new ArgumentException("KnightGif <frames-directory> <output.gif> [fps=8]");
            string[] files = Directory.GetFiles(args[0], "*.png");
            Array.Sort(files, StringComparer.Ordinal);
            if (files.Length == 0) throw new ArgumentException("No preview frames found.");
            int fps = args.Length > 2 ? int.Parse(args[2]) : 8;
            if (fps < 1 || fps > 100) throw new ArgumentException("fps must be 1..100.");
            int width = 0, height = 0;
            using (BinaryWriter writer = new BinaryWriter(File.Create(args[1])))
            {
                for (int frame = 0; frame < files.Length; frame++)
                using (Image image = Image.FromFile(files[frame]))
                using (MemoryStream memory = new MemoryStream())
                {
                    if (frame == 0)
                    {
                        width = image.Width; height = image.Height;
                        writer.Write(Encoding.ASCII.GetBytes("GIF89a"));
                        writer.Write((ushort)width); writer.Write((ushort)height);
                        writer.Write(new byte[] { 0x70, 0, 0 }); // Local palettes on every frame.
                        writer.Write(new byte[] { 0x21, 0xff, 11 });
                        writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                        writer.Write(new byte[] { 3, 1, 0, 0, 0 }); // Loop indefinitely.
                    }
                    if (image.Width != width || image.Height != height) throw new InvalidDataException("All frames must have equal dimensions.");
                    image.Save(memory, ImageFormat.Gif);
                    WriteFrame(writer, memory.ToArray(), frame, fps);
                }
                writer.Write((byte)0x3b);
            }
            using (Image check = Image.FromFile(args[1]))
            {
                int count = check.GetFrameCount(FrameDimension.Time);
                if (count != files.Length) throw new InvalidDataException("Encoded frame count differs.");
                for (int index = 0; index < count; index++) check.SelectActiveFrame(FrameDimension.Time, index);
                Console.WriteLine("GIF verified: {0} frames, {1}x{2}, {3} fps, {4:N0} bytes", count, width, height, fps, new FileInfo(args[1]).Length);
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
