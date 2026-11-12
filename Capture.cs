using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Knipser;

internal static class Capture
{
    public static Rectangle VirtualScreen => Forms.SystemInformation.VirtualScreen;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public int x, y; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO ci);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr h, out ICONINFO ii);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int w, int h, int step, IntPtr br, int flags);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

    /// <summary>Zeichnet den Mauszeiger (falls sichtbar) in das Bild, das den Bildschirmbereich r zeigt.</summary>
    static void DrawCursor(Graphics g, Rectangle r)
    {
        var ci = new CURSORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci) || (ci.flags & 1) == 0 || ci.hCursor == IntPtr.Zero) return;
        int hx = 0, hy = 0;
        if (GetIconInfo(ci.hCursor, out var ii))
        {
            hx = ii.xHotspot; hy = ii.yHotspot;
            if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
            if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
        }
        var hdc = g.GetHdc();
        try { DrawIconEx(hdc, ci.x - hx - r.X, ci.y - hy - r.Y, ci.hCursor, 0, 0, 0, IntPtr.Zero, 3); }
        finally { g.ReleaseHdc(hdc); }
    }

    public static BitmapSource Grab(Rectangle r, bool withCursor = false)
    {
        using var bmp = new Bitmap(r.Width, r.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size, CopyPixelOperation.SourceCopy);
            if (withCursor) DrawCursor(g, r);
        }

        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var src = BitmapSource.Create(bmp.Width, bmp.Height, 96, 96, PixelFormats.Bgra32, null,
                data.Scan0, data.Stride * bmp.Height, data.Stride);
            // Alpha ist bei Bildschirmfotos immer deckend
            var opaque = new FormatConvertedBitmap(src, PixelFormats.Bgr32, null, 0);
            opaque.Freeze();
            return opaque;
        }
        finally { bmp.UnlockBits(data); }
    }

    /// <summary>Rechteck (in Pixeln) des Fensters unter dem Mauszeiger.</summary>
    public static Rectangle? WindowUnderCursor()
    {
        if (!Native.GetCursorPos(out var p)) return null;
        var h = Native.WindowFromPoint(p);
        if (h == IntPtr.Zero) return null;
        h = Native.GetAncestor(h, 2); // GA_ROOT
        if (h == IntPtr.Zero) return null;
        if (Native.DwmGetWindowAttribute(h, 9, out var r, 16) != 0 && !Native.GetWindowRect(h, out r)) return null;
        var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        rect.Intersect(VirtualScreen);
        return rect.Width > 1 && rect.Height > 1 ? rect : null;
    }
}

