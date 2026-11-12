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

    public static BitmapSource Grab(Rectangle r)
    {
        using var bmp = new Bitmap(r.Width, r.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size, CopyPixelOperation.SourceCopy);

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

