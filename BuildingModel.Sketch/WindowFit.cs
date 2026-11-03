using System;
using System.Windows;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// Keeps a window on the screen: never larger than the work area (the screen less the taskbar), and moved back inside it
    /// when it opens, title bar first (it holds the close button). A window asking for more than the screen has, opened
    /// centred on its owner, otherwise puts its title bar above the top of the screen and cannot be closed or moved.
    /// </summary>
    public static class WindowFit
    {
        /// <summary>The share of the work area a window may take at most.</summary>
        public const double MaxShare = 0.95;

        /// <summary>The wanted size, or <see cref="MaxShare"/> of the room there is when that is less (no room known: as wanted).</summary>
        public static double Size(double wanted, double room) =>
            room > 0 && !double.IsNaN(wanted) ? Math.Min(wanted, Math.Floor(room * MaxShare)) : wanted;

        /// <summary>
        /// <paramref name="window"/> moved inside <paramref name="area"/>: off the right and bottom first, then the left and top,
        /// so a window larger than the area keeps its top-left (title bar) on screen.
        /// </summary>
        public static Rect OnScreen(Rect window, Rect area)
        {
            double x = window.X, y = window.Y;
            if (x + window.Width > area.Right) x = area.Right - window.Width;
            if (y + window.Height > area.Bottom) y = area.Bottom - window.Height;
            if (x < area.Left) x = area.Left;
            if (y < area.Top) y = area.Top;
            return new Rect(x, y, window.Width, window.Height);
        }

        /// <summary>
        /// Fits <paramref name="w"/> to the screen: a set size is capped at the work area (its minimum too), a window sized to
        /// its content may not grow past it, and once open it is moved back on screen. Returns the window.
        /// </summary>
        public static T Fit<T>(T w) where T : Window
        {
            var area = SystemParameters.WorkArea;
            if (!double.IsNaN(w.Width)) w.Width = Size(w.Width, area.Width);
            if (!double.IsNaN(w.Height)) w.Height = Size(w.Height, area.Height);
            w.MinWidth = Math.Min(w.MinWidth, Size(area.Width, area.Width));
            w.MinHeight = Math.Min(w.MinHeight, Size(area.Height, area.Height));
            w.MaxWidth = Math.Min(w.MaxWidth, area.Width);
            w.MaxHeight = Math.Min(w.MaxHeight, area.Height);
            void Keep()
            {
                if (w.WindowState != WindowState.Normal) return;
                var now = SystemParameters.WorkArea;
                var r = OnScreen(new Rect(w.Left, w.Top, w.ActualWidth > 0 ? w.ActualWidth : w.Width, w.ActualHeight > 0 ? w.ActualHeight : w.Height), now);
                if (Math.Abs(r.X - w.Left) > 0.5) w.Left = r.X;
                if (Math.Abs(r.Y - w.Top) > 0.5) w.Top = r.Y;
            }
            w.Loaded += (s, e) => Keep();
            w.ContentRendered += (s, e) => Keep(); // a window sized to its content has its size only now
            return w;
        }
    }
}
