using System.Collections.Generic;
using System.IO;
using System.Windows;
using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.Instructions;
using OpenTail.Bullshark.Core.Trust;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The setup check and the instructions editor (docs/plans/setup-check.md). Both read Claude Code's files and change nothing here. They are opened
/// from the Claude sub-tab of Settings (2026-11-18); the dialogs and what they show are as before.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The setup check: Claude Code's settings, hooks, MCP servers and CLAUDE.md, read only.</summary>
    private void OpenSetupCheck()
    {
        var dialog = new SetupCheckDialog(Palette, () => SetupCheck.Run(SetupCheck.DefaultClaudeDir, SetupCheck.DefaultClaudeJsonPath))
        {
            Owner = this,
        };
        dialog.ShowDialog();
    }

    /// <summary>The instructions editor: the user's CLAUDE.md, then the project files of the folders Claude has been trusted in.</summary>
    private void OpenInstructions()
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
