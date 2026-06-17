using System.Drawing;
using System.Drawing.Drawing2D;

namespace Shuush;

/// <summary>
/// Renders the colored-dot tray icon. Each render allocates a transient GDI icon
/// handle which is freed immediately after cloning into a self-contained managed
/// <see cref="Icon"/>, so callers can dispose the returned icon without tracking
/// native handles.
/// </summary>
internal static class TrayIconRenderer
{
    /// <summary>
    /// Builds a tray icon: a filled circle in <paramref name="fill"/>. When
    /// <paramref name="paused"/> is set, two pause bars are overlaid.
    /// </summary>
    public static Icon Create(Color fill, bool paused)
    {
        const int Size = 32;
        using Bitmap bmp = new(Size, Size);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            Rectangle dot = new(3, 3, Size - 6, Size - 6);
            using (Brush body = new SolidBrush(fill))
            {
                g.FillEllipse(body, dot);
            }

            using (Pen ring = new(Color.FromArgb(170, 0, 0, 0), 2f))
            {
                g.DrawEllipse(ring, dot);
            }

            if (paused)
            {
                using Brush bar = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
                g.FillRectangle(bar, 11, 10, 4, 12);
                g.FillRectangle(bar, 17, 10, 4, 12);
            }
        }

        IntPtr handle = bmp.GetHicon();
        try
        {
            using Icon transient = Icon.FromHandle(handle);
            return (Icon)transient.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}
