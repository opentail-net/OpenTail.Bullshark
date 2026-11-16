using System.Collections.Generic;
using System.IO;
using System.Windows;
using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.Instructions;
using OpenTail.Bullshark.Core.Trust;

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

    /// <summary>The instructions editor: the user's CLAUDE.md, then the project files of the folders Claude has been trusted in.</summary>
    private void InstructionsButton_Click(object sender, RoutedEventArgs e)
    {
        var entries = new List<InstructionEntry> { new("Your instructions (all projects)", InstructionFiles.UserPath) };
        foreach (var folder in TrustCheck.TrustedRepositories(SetupCheck.DefaultClaudeJsonPath))
        {
            if (!Directory.Exists(folder)) continue;
            var name = Path.GetFileName(folder.TrimEnd('/', '\\'));
            entries.Add(new InstructionEntry($"{name}  ({folder})", InstructionFiles.ProjectPath(folder)));
        }
        var dialog = new InstructionsDialog(Palette, entries) { Owner = this };
        dialog.ShowDialog();
    }
}
