using System.Windows;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The App's resources for a headless window (dedup review item 4, 2026-11-23). A window's XAML needs the shared styles when the window is made.
/// Used by every test file that makes a resume screen or another window.
/// </summary>
internal static class AppResources
{
    /// <summary>
    /// Makes the application and loads the shared styles, once per test process. The application does not shut down when its last window closes:
    /// the next test could not make another one.
    /// </summary>
    public static void Ensure()
    {
        if (Application.Current is not null) return;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/OpenTail.Bullshark.App;component/Styles/Controls.xaml", UriKind.Absolute),
        });
    }
}
