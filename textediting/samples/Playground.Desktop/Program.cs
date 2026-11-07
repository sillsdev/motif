using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using SIL.TextEditing.Playground;

namespace SIL.TextEditing.Samples.PlaygroundDesktop
{
    /// <summary>Options: <c>--scenario "name"</c> opens that scenario; <c>--dev</c> opens in developer mode; <c>--minimized</c> starts minimized without
    /// taking focus (for smoke runs).</summary>
    sealed class DesktopApp : PlaygroundApp
    {
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var args = desktop.Args ?? new string[0];
                int s = Array.IndexOf(args, "--scenario");
                var view = new PlaygroundView(s >= 0 && s + 1 < args.Length ? args[s + 1] : Scenarios.MixedBidi, args.Contains("--dev"));
                var window = new Window
                {
                    Title = "SIL.TextEditing playground (" + RuntimeLabel + ", Avalonia " + AvaloniaVersion + ")",
                    Width = 1100,
                    Height = 720,
                    Content = view,
                };
                if (args.Contains("--minimized"))
                {
                    window.ShowActivated = false;
                    window.WindowState = WindowState.Minimized;
                }
                desktop.MainWindow = window;
            }
            base.OnFrameworkInitializationCompleted();
        }

        static string RuntimeLabel =>
#if NET10_0_OR_GREATER
            "net10.0";
#else
            "net48";
#endif
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args) =>
            AppBuilder.Configure<DesktopApp>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}
