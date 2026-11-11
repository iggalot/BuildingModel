using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BuildingModel.Diagnostics;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// The suite's Help menu, the last in every app's menu bar (owner 2026-11-11: one order everywhere, File, Edit, the app's
    /// own menus, View, Help): Open the log folder, and About (the app, its version, what it follows, the log folder).
    /// </summary>
    public static class HelpMenu
    {
        public static MenuItem For(Window owner, string appName, string about)
        {
            var help = new MenuItem { Header = "_Help" };
            var logs = new MenuItem { Header = "Open the _log folder", ToolTip = "The program's logs: what it did and any problem it met" };
            logs.Click += (s, e) => OpenLogFolder(owner);
            help.Items.Add(logs);
            var aboutItem = new MenuItem { Header = "_About" };
            aboutItem.Click += (s, e) => About(owner, appName, about);
            help.Items.Add(aboutItem);
            return help;
        }

        public static void OpenLogFolder(Window owner)
        {
            try
            {
                Directory.CreateDirectory(Log.Folder);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + Log.Folder + "\"");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
            {
                Log.Warning("App", "Could not open the log folder", ex);
                MessageBox.Show(owner, "The log folder is:\n" + Log.Folder, "Log folder", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public static void About(Window owner, string appName, string about)
        {
            var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            MessageBox.Show(owner, $"{appName} {version}\n\n" + (string.IsNullOrEmpty(about) ? "" : about + "\n\n") + "Log: " + Log.Folder,
                            "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
