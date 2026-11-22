using System.Collections.Generic;
using System.IO;
using System.Windows;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.Instructions;
using OpenTail.Bullshark.Core.Trust;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The setup check and the instructions editor (docs/plans/Done/setup-check.md). Both read Claude Code's files and change nothing here. They are opened
/// from the Claude sub-tab of Settings (2026-11-18); the dialogs and what they show are as before.
/// </summary>
public partial class SessionWindow
{
    /// <summary>
    /// The setup check. Under Claude it is Claude Code's settings, hooks, MCP servers and CLAUDE.md, read only. Under Codex it is the one Codex row:
    /// whether the codex program is on PATH (C-D7). Neither starts a program.
    /// </summary>
    private void OpenSetupCheck()
    {
        var dialog = IsCodexActive
            ? new SetupCheckDialog(Palette, () => CodexSetupCheck.Run(),
                "Looks for the Codex CLI on PATH and changes nothing. Its version is not read, and codex is not started.")
            : IsCopilotActive
                // The Copilot row: the one row, the bundled version read through cmd.exe with no update check (G1.6, G2.2, C-D7). It runs
                // asynchronously: a blocking wait here would stall the UI thread, and the check's continuations need that thread (a deadlock).
                ? new SetupCheckDialog(Palette, run: null,
                    noteText: "Looks for the Copilot CLI on PATH and reads the version it bundles. No session is started.",
                    runAsync: () => CopilotSetupCheck.RunAsync(new ProcessCliRunner("cmd.exe")))
                : new SetupCheckDialog(Palette, () => SetupCheck.Run(SetupCheck.DefaultClaudeDir, SetupCheck.DefaultClaudeJsonPath));
        dialog.Owner = this;
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
