using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace Knipser;

/// <summary>
/// Scrollende Aufnahme: Bereich wiederholt abfotografieren, dazwischen per Mausrad scrollen, Bilder zusammensetzen.
/// Der Abgleich ist unscharf (toleriert animierte Hintergründe, Farbverläufe, feste Leisten), damit auch
/// moderne Webseiten funktionieren.
/// </summary>
internal static class ScrollCapture
{
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, int data, int extra);
    const uint MOUSEEVENTF_WHEEL = 0x0800;

    const int MaxFrames = 80;
    const int MaxHeight = 30000;
    const int Samples = 256;       // Spalten pro Zeile für den Vergleich
    const int Tol = 14;            // erlaubte Abweichung je Farbkanal

    public static async Task<BitmapSource?> Run(Drawing.Rectangle r)
    {
        Log($"Start Bereich {r.Width}x{r.Height} bei {r.X},{r.Y}");
        SetCursorPos(r.X + r.Width / 2, r.Y + r.Height / 2);
        await Task.Delay(350);

        int w = r.Width, h = r.Height;
        var first = await Settle(r, w, h);
        var parts = new List<byte[]> { first };   // zusammengesetzte Zeilenblöcke
        int totalRows = h;
        var prev = first;
        int lastShift = 0, fallbacks = 0;
        // Browser scrollen pro Mausrad-Raste 100 Pixel (bei Skalierung entsprechend mehr): 3 Rasten = 300 Pixel
        double scale = 1;
        try { using var g = Drawing.Graphics.FromHwnd(IntPtr.Zero); scale = g.DpiX / 96.0; } catch { }
        int expected = (int)Math.Round(300 * scale);

        for (int i = 0; i < MaxFrames && totalRows < MaxHeight; i++)
        {
            // Mausrad drehen und warten, bis die Seite (auch bei weichem Scrollen) still steht.
            // Hat sich nichts getan, bis zu dreimal nachlegen, bevor wir das Seitenende annehmen.
            byte[]? cur = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, -120 * 3, 0);
                await Task.Delay(300);
                cur = await Settle(r, w, h);
                if (!Similar(prev, cur)) break;
                await Task.Delay(300);
            }
            if (cur == null || Similar(prev, cur)) { Log($"Bild {i + 1}: keine Bewegung -> Ende"); break; }   // Ende der Seite

            int s = FindShift(prev, cur, w, h, lastShift > 0 ? lastShift : expected);
            Log($"Bild {i + 1}: Verschiebung {s} px");
            if (s < 0) break;                  // Seitenende
            if (s == 0)
            {
                // Inhalt ändert sich beim Scrollen (Animationen, festgepinnte Abschnitte): übliche Schrittweite annehmen
                if (lastShift > 0 && fallbacks < 2 && lastShift < h - 8)
                {
                    s = lastShift; fallbacks++;
                    Log($"  nicht zuordenbar -> übliche Schrittweite {s} px angenommen");
                }
                else break;
            }
            else fallbacks = 0;
            lastShift = s;
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

    static void Log(string msg)
    {
        try
        {
            var f = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Knipser", "scroll.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f)!);
            System.IO.File.AppendAllText(f, $"{DateTime.Now:HH:mm:ss} {msg}\r\n");
        }
        catch { }
    }

    /// <summary>Fotografiert so lange, bis zwei Aufnahmen hintereinander (nahezu) gleich sind.</summary>
    static async Task<byte[]> Settle(Drawing.Rectangle r, int w, int h)
    {
        var a = Pixels(Capture.Grab(r), w, h);
        for (int i = 0; i < 12; i++)
        {
            await Task.Delay(120);
            var b = Pixels(Capture.Grab(r), w, h);
            if (Similar(a, b)) return b;
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

    /// <summary>Nahezu gleich: höchstens 0,3 % der Farbwerte weichen merklich ab (animierte Hintergründe zählen nicht).</summary>
    static bool Similar(byte[] a, byte[] b)
    {
        int diff = 0, limit = a.Length / 330;
        for (int i = 0; i < a.Length; i += 4)
        {
            if (Math.Abs(a[i] - b[i]) > Tol || Math.Abs(a[i + 1] - b[i + 1]) > Tol || Math.Abs(a[i + 2] - b[i + 2]) > Tol)
            {
                if (++diff > limit / 4) return false;
            }
        }
        return true;
    }

    /// <summary>Je Zeile Stichproben (B,G,R) und "Inhalt" = Anzahl Stichproben, die vom Zeilenhintergrund abweichen.</summary>
    static (byte[] sig, int[] ink, int k) Signature(byte[] px, int w, int h)
    {
        int k = Math.Min(Samples, w);
        var sig = new byte[h * k * 3];
        var ink = new int[h];
        var cols = new int[k];
        for (int j = 0; j < k; j++) cols[j] = (int)((long)j * w / k);
        var tmpB = new byte[k]; var tmpG = new byte[k]; var tmpR = new byte[k];
        for (int y = 0; y < h; y++)
        {
            int o = y * w * 4, so = y * k * 3;
            for (int j = 0; j < k; j++)
            {
                int p = o + cols[j] * 4;
                byte b = px[p], g = px[p + 1], r = px[p + 2];
                sig[so + j * 3] = b; sig[so + j * 3 + 1] = g; sig[so + j * 3 + 2] = r;
                tmpB[j] = b; tmpG[j] = g; tmpR[j] = r;
            }
            Array.Sort(tmpB); Array.Sort(tmpG); Array.Sort(tmpR);
            int mb = tmpB[k / 2], mg = tmpG[k / 2], mr = tmpR[k / 2], n = 0;
            for (int j = 0; j < k; j++)
            {
                if (Math.Abs(sig[so + j * 3] - mb) > 24 || Math.Abs(sig[so + j * 3 + 1] - mg) > 24 || Math.Abs(sig[so + j * 3 + 2] - mr) > 24) n++;
            }
            ink[y] = n;
        }
        return (sig, ink, k);
    }

    /// <summary>
    /// Um wie viele Zeilen ist cur gegenüber prev nach oben gewandert? 0 = nicht gefunden.
    /// Zeilen mit Inhalt (Text, Bilder) zählen, leere Zeilen nicht; kleine Abweichungen werden verziehen.
    /// </summary>
    static int FindShift(byte[] prev, byte[] cur, int w, int h, int hint = 0)
    {
        var (sa, _, k) = Signature(prev, w, h);
        var (sb, inkB, _) = Signature(cur, w, h);
        int stride = k * 3;

        long Score(int s, int tol, out long total)
        {
            int n = h - s;
            long score = 0; total = 0;
            for (int y = 0; y < n; y++)
            {
                int ink = inkB[y];
                if (ink == 0) continue;
                total += ink;
                int limit = Math.Max(2, ink / 6);
                int oa = (y + s) * stride, ob = y * stride, bad = 0;
                for (int j = 0; j < stride; j += 3)
                {
                    if (Math.Abs(sa[oa + j] - sb[ob + j]) > tol || Math.Abs(sa[oa + j + 1] - sb[ob + j + 1]) > tol || Math.Abs(sa[oa + j + 2] - sb[ob + j + 2]) > tol)
                    {
                        if (++bad > limit) break;
                    }
                }
                if (bad <= limit) score += ink;
            }
            return score;
        }

        // Steht der Inhalt (bis auf Animationen) still? Dann ist das Seitenende erreicht.
        long still = Score(0, Tol, out long stillTotal);
        if (stillTotal > 0 && (double)still / stillTotal >= 0.6) { Log("  Inhalt unverändert -> Seitenende"); return -1; }

        var scores = new long[h];
        long[] totals = new long[h];
        long bestScore = 0; int bestS = 0;
        for (int s = 1; s < h - 8; s++)
        {
            scores[s] = Score(s, Tol, out totals[s]);
            if (scores[s] > bestScore) { bestScore = scores[s]; bestS = s; }
        }
        if (bestS == 0) { Log("  keine Übereinstimmung"); return 0; }

        long second = 0;
        for (int s = 1; s < h - 8; s++) if (Math.Abs(s - bestS) > 3 && scores[s] > second) second = scores[s];
        double ratio = (double)bestScore / Math.Max(totals[bestS], 1);
        bool distinct = bestScore >= 1.5 * second;
        Log($"  beste Verschiebung {bestS}, Übereinstimmung {ratio:P0}, Abstand zum Zweiten {(second == 0 ? "∞" : (bestScore / (double)second).ToString("0.0"))}");
        if (bestScore < 20 || !(ratio >= 0.45 || (distinct && ratio >= 0.10))) return 0;

        // Bisherige Schrittweite bevorzugen (Mausrad scrollt immer gleich weit)
        if (hint > 0 && hint < h - 8 && scores[hint] >= 0.5 * bestScore) bestS = hint;
        else
        {
            // auf das Pixel genau machen: strenger Vergleich in der Nachbarschaft
            long bestStrict = -1;
            for (int s = Math.Max(1, bestS - 2); s <= Math.Min(h - 9, bestS + 2); s++)
            {
                long sc = Score(s, 3, out _);
                if (sc > bestStrict) { bestStrict = sc; bestS = s; }
            }
        }
        return bestS;
    }
}
