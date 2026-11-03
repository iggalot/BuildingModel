using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BuildingModel.Sketch
{
    /// <summary>A small window asking for one line of text (a note, a distance). Null when cancelled.</summary>
    public static class TextPrompt
    {
        public static string Ask(string question, string initial)
        {
            var box = new TextBox { Text = initial ?? "", MinWidth = 280, Margin = new Thickness(0, 6, 0, 10), Padding = new Thickness(3) };
            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            var panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 });
            panel.Children.Add(box);
            panel.Children.Add(buttons);
            var w = new Window
            {
                Title = question,
                Content = panel,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
                Owner = Application.Current?.MainWindow,
            };
            WindowFit.Fit(w); // on screen whatever the content
            ok.Click += (s, e) => w.DialogResult = true;
            w.Loaded += (s, e) => { box.Focus(); box.SelectAll(); Keyboard.Focus(box); };
            return w.ShowDialog() == true ? box.Text : null;
        }
    }
}
