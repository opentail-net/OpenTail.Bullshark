using System.ComponentModel;
using System.Diagnostics;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;

namespace OpenTail.Bullshark.App;

/// <summary>
/// Quick capture (batch B3, ideas #7): "Add idea…" and "Open ideas file" in the command palette. The idea is appended to ideas.md beside the state
/// file. Nothing is sent to a session. A failure is said in the status line and logged, never thrown into the window. The idea's text is not logged.
/// </summary>
public partial class SessionWindow
{
    /// <summary>Asks for an idea in a box and appends it to ideas.md. Cancel writes nothing.</summary>
    private void AddIdea()
    {
        var dialog = new IdeaDialog(Palette) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var path = IdeaNotes.PathFor(_options.StatePath);
        try
        {
            var saved = IdeaNotes.Append(path, dialog.Text, DateTimeOffset.Now);
            SetStatus(saved ? "Idea saved." : "Nothing saved: the idea was empty.");
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"idea not saved: {ex.GetType().Name}");
            SetStatus($"The idea was not saved: {ex.Message}");
        }
    }

    /// <summary>Creates ideas.md when it is missing, then opens it in the program the system uses for .md files.</summary>
    private void OpenIdeas()
    {
        var path = IdeaNotes.PathFor(_options.StatePath);
        try
        {
            IdeaNotes.Ensure(path);
            var open = IdeaNotes.ReadOpen(path).Count;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
            SetStatus($"Opened ideas.md: {open} open {(open == 1 ? "idea" : "ideas")}.");
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex) || ex is Win32Exception)
        {
            AppLog.Write($"ideas file not opened: {ex.GetType().Name}");
            SetStatus($"The ideas file was not opened: {ex.Message}");
        }
    }
}
