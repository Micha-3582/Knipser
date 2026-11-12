using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace Knipser;

/// <summary>Scrollende Aufnahme: Bereich wiederholt abfotografieren, dazwischen per Mausrad scrollen, Bilder zusammensetzen.</summary>
internal static class ScrollCapture
{
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, int data, int extra);
    const uint MOUSEEVENTF_WHEEL = 0x0800;

    const int MaxFrames = 80;
    const int MaxHeight = 30000;

    public static async Task<BitmapSource?> Run(Drawing.Rectangle r)
    {
        SetCursorPos(r.X + r.Width / 2, r.Y + r.Height / 2);
        await Task.Delay(300);

        int w = r.Width, h = r.Height;
        var first = Pixels(Capture.Grab(r), w, h);
        var parts = new List<byte[]> { first };   // zusammengesetzte Zeilenblöcke
        int totalRows = h;
        var prev = first;

        for (int i = 0; i < MaxFrames && totalRows < MaxHeight; i++)
        {
            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, -120 * 3, 0);
            await Task.Delay(350);
            var cur = Pixels(Capture.Grab(r), w, h);

            int s = FindShift(prev, cur, w, h);
            if (s <= 0) break;                 // nichts mehr gescrollt oder nicht zuordenbar
            var add = new byte[s * w * 4];
            Buffer.BlockCopy(cur, (h - s) * w * 4, add, 0, add.Length);
            parts.Add(add);
            totalRows += s;
            prev = cur;
        }

        var all = new byte[totalRows * w * 4];
        int off = 0;
        foreach (var p in parts) { Buffer.BlockCopy(p, 0, all, off, p.Length); off += p.Length; }
        var bmp = BitmapSource.Create(w, totalRows, 96, 96, PixelFormats.Bgr32, null, all, w * 4);
        bmp.Freeze();
        return bmp;
    }

    static byte[] Pixels(BitmapSource src, int w, int h)
    {
        var buf = new byte[w * h * 4];
        src.CopyPixels(buf, w * 4, 0);
        return buf;
    }

    static ulong[] RowHashes(byte[] px, int w, int h)
    {
        var hs = new ulong[h];
        int stride = w * 4;
        for (int y = 0; y < h; y++)
        {
            ulong x = 1469598103934665603UL;
            int o = y * stride;
            for (int i = 0; i < stride; i++) { x ^= px[o + i]; x *= 1099511628211UL; }
            hs[y] = x;
        }
        return hs;
    }

    /// <summary>Um wie viele Zeilen ist cur gegenüber prev nach oben gewandert? 0 = gleich/nicht gefunden.</summary>
    static int FindShift(byte[] prev, byte[] cur, int w, int h)
    {
        var a = RowHashes(prev, w, h);
        var b = RowHashes(cur, w, h);

        bool same = true;
        for (int y = 0; y < h; y++) if (a[y] != b[y]) { same = false; break; }
        if (same) return 0;

        // Zuerst große Überlappung (kleine Verschiebung) prüfen
        for (int s = 1; s < h - 8; s++)
        {
            int n = h - s, bad = 0, limit = n / 50;   // 2 % Abweichung erlaubt (Cursor, Animationen)
            for (int y = 0; y < n; y++)
            {
                if (a[y + s] != b[y] && ++bad > limit) break;
            }
            if (bad <= limit) return s;
        }
        return 0;
    }
}
