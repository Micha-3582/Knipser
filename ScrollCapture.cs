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
        await Task.Delay(350);

        int w = r.Width, h = r.Height;
        var first = await Settle(r, w, h);
        var parts = new List<byte[]> { first };   // zusammengesetzte Zeilenblöcke
        int totalRows = h;
        var prev = first;

        for (int i = 0; i < MaxFrames && totalRows < MaxHeight; i++)
        {
            // Mausrad drehen und warten, bis die Seite (auch bei weichem Scrollen) wirklich still steht.
            // Hat sich nichts getan, einmal nachlegen, bevor wir das Seitenende annehmen.
            byte[]? cur = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, -120 * 3, 0);
                await Task.Delay(300);
                cur = await Settle(r, w, h);
                if (!cur.AsSpan().SequenceEqual(prev)) break;
                await Task.Delay(300);
            }
            if (cur == null || cur.AsSpan().SequenceEqual(prev)) break;   // Ende der Seite

            int s = FindShift(prev, cur, w, h);
            if (s <= 0) break;                 // nicht zuordenbar
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

    /// <summary>Fotografiert so lange, bis zwei Aufnahmen hintereinander gleich sind (Scroll-Animation vorbei).</summary>
    static async Task<byte[]> Settle(Drawing.Rectangle r, int w, int h)
    {
        var a = Pixels(Capture.Grab(r), w, h);
        for (int i = 0; i < 15; i++)
        {
            await Task.Delay(120);
            var b = Pixels(Capture.Grab(r), w, h);
            if (a.AsSpan().SequenceEqual(b)) return b;
            a = b;
        }
        return a;
    }

    static byte[] Pixels(BitmapSource src, int w, int h)
    {
        var buf = new byte[w * h * 4];
        src.CopyPixels(buf, w * 4, 0);
        return buf;
    }

    /// <summary>Zeilen-Hashes plus Kennzeichen, ob die Zeile Inhalt hat (nicht einfarbig).</summary>
    static (ulong[] hash, bool[] info) RowInfo(byte[] px, int w, int h)
    {
        var hs = new ulong[h];
        var info = new bool[h];
        int stride = w * 4;
        for (int y = 0; y < h; y++)
        {
            ulong x = 1469598103934665603UL;
            int o = y * stride;
            bool uniform = true;
            for (int i = 0; i < stride; i++)
            {
                byte v = px[o + i];
                x ^= v; x *= 1099511628211UL;
                if (uniform && i >= 4 && v != px[o + (i & 3)]) uniform = false;
            }
            hs[y] = x;
            info[y] = !uniform;
        }
        return (hs, info);
    }

    /// <summary>
    /// Um wie viele Zeilen ist cur gegenüber prev nach oben gewandert? 0 = nicht gefunden.
    /// Zählt übereinstimmende Zeilen mit Inhalt; feste Kopf-/Fußleisten und Werbung stören dadurch kaum.
    /// </summary>
    static int FindShift(byte[] prev, byte[] cur, int w, int h)
    {
        var (a, _) = RowInfo(prev, w, h);
        var (b, infoB) = RowInfo(cur, w, h);

        int bestS = 0, bestMatches = 0;
        double bestRatio = 0;
        for (int s = 1; s < h - 8; s++)
        {
            int n = h - s, info = 0, match = 0;
            for (int y = 0; y < n; y++)
            {
                if (!infoB[y]) continue;
                info++;
                if (a[y + s] == b[y]) match++;
            }
            if (info == 0) continue;
            if (match > bestMatches) { bestMatches = match; bestS = s; bestRatio = (double)match / info; }
        }
        return bestMatches >= 3 && bestRatio >= 0.55 ? bestS : 0;
    }
}
