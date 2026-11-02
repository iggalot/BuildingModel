using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BuildingModel.Sketch
{
    /// <summary>The colours and text styles of the plan import dialogs (moved from the Shear Wall Calculator with them).</summary>
    internal static class DialogUi
    {
        public static readonly Brush Ink = Frozen("#0B0B0B"), InkSecondary = Frozen("#52514E"), InkMuted = Frozen("#7A7974"),
                                     Hairline = Frozen("#D5DAE1"), Surface = Frozen("#F7F8FA"), WarnInk = Frozen("#8A5300");

        private static Brush Frozen(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        public static TextBlock Heading(string text) => new TextBlock
        {
            Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ink, Margin = new Thickness(0, 0, 0, 6),
        };

        public static TextBlock Note(string text) => new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, Foreground = InkSecondary, Margin = new Thickness(0, 0, 0, 8),
        };
    }
}
