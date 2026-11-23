using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace TouchTypingStudio;

internal sealed partial class LabScene
{
    private string captureImage;
    internal string ClipboardImage => DebugCaptureImage ??
        (Clipboard.StartsWith("Снимок-", StringComparison.Ordinal) && Clipboard.EndsWith(".png", StringComparison.Ordinal)
        ? Terminal.ReadFile("~/Pictures/Screenshots/" + Clipboard) : null);

    internal void StartCapture(string png)
    {
        // Native clients render only the simulated desktop. A protocol-only session has no widget.
        captureImage = png is { Length: < 1400000 } && png.StartsWith("iVBORw0KGgo", StringComparison.Ordinal)
            ? "data:image/png;base64," + png : null;
    }

    private void Capture()
    {
        Clipboard = "Снимок-" + Screenshots + ".png";
        Terminal.StoreFile("~/Pictures/Screenshots/" + Clipboard, captureImage ?? DeskThumbnail());
        captureImage = null;
    }

    private string DeskThumbnail()
    {
        const int width = 120, height = 72;
        var scan = new byte[(width + 1) * height];
        void Fill(int x, int y, int w, int h, byte color)
        {
            for (int row = y; row < Math.Min(height, y + h); row++)
                Array.Fill(scan, color, row * (width + 1) + x + 1, Math.Min(w, width - x));
        }
        Fill(0, 0, width, height, 1); Fill(0, 0, width, 5, 2); Fill(0, 5, 6, 67, 2);
        int i = 0;
        foreach (var w in Windows)
        {
            if (w.Workspace != Workspace || w.State == "minimized" || ShowDesktop) continue;
            int x = w.State == "right" ? 64 : 8, y = 8 + i++ * 2;
            int size = w.State is "left" or "right" ? 52 : 99;
            Fill(x, y, size, 54, 2);
            Fill(x + 1, y + 4, size - 2, 49, (byte)(w.App switch { "code" => 3, "terminal" => 4, _ => 5 }));
        }
        using var output = new MemoryStream();
        output.Write(new byte[] {137,80,78,71,13,10,26,10});
        void Chunk(string tag, byte[] bytes)
        {
            byte[] header = System.Text.Encoding.ASCII.GetBytes(tag);
            byte[] integer = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(integer, bytes.Length); output.Write(integer);
            uint crc = uint.MaxValue;
            foreach (byte[] part in new[] { header, bytes })
            {
                output.Write(part);
                foreach (byte b in part) { crc ^= b; for (int j = 0; j < 8; j++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u); }
            }
            BinaryPrimitives.WriteUInt32BigEndian(integer, ~crc); output.Write(integer);
        }
        Chunk("IHDR", [0,0,0,width,0,0,0,height,8,3,0,0,0]);
        Chunk("PLTE", [0,0,0,65,35,57,36,36,41,31,31,31,44,16,40,248,249,251]);
        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Fastest, true)) deflate.Write(scan);
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []);
        return "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
    }
}
