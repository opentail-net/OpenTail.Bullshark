using System.Windows;
using OpenTail.Bullshark.Core.Health;

namespace OpenTail.Bullshark.App;

/// <summary>The setup check from the status bar (docs/plans/setup-check.md). It reads Claude Code's files and changes nothing.</summary>
public partial class SessionWindow
{
    private void SetupCheckButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SetupCheckDialog(Palette, () => SetupCheck.Run(SetupCheck.DefaultClaudeDir, SetupCheck.DefaultClaudeJsonPath))
        {
            Owner = this,
        };
        dialog.ShowDialog();
    }
}
