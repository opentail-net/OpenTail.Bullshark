using System.Collections.Generic;
using System.IO;
using System.Windows;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.Instructions;
using OpenTail.Bullshark.Core.OpenCode;
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
    /// whether the codex program is on PATH (C-D7). Under opencode it is the one opencode row (OC-D1). No row starts a program.
    /// </summary>
    private void OpenSetupCheck()
    {
        var dialog = IsCodexActive
            ? new SetupCheckDialog(Palette, () => CodexSetupCheck.Run(),
                "Looks for the Codex CLI on PATH and changes nothing. Its version is not read, and codex is not started.")
            : IsCopilotActive
                // The Copilot rows: the terminal copy's version read through cmd.exe, and the start copy's (copilot.ps1) through pwsh, both with no update check (G1.6, G2.2, C-D7). It runs
                // asynchronously: a blocking wait here would stall the UI thread, and the check's continuations need that thread (a deadlock).
                ? new SetupCheckDialog(Palette, run: null,
                    noteText: "Looks for the Copilot CLI on PATH and reads the version of each copy the app runs. No session is started.",
                    runAsync: () => CopilotSetupCheck.RunAsync(new ProcessCliRunner("cmd.exe"), new ProcessCliRunner("pwsh.exe"), log: AppLog.Write))
                : IsOpenCodeActive
                    // The opencode row: the shim FindShimOnPath finds (or none), and the version read from the npm package file beside it (OC-D1). The path
                    // is shown on this page only; nothing is logged and no program is started.
                    ? new SetupCheckDialog(Palette, () => OpenCodeRows(OpenCodeSetupCheck.FindShimOnPath(), ReadPackageText),
                        noteText: "Looks for the opencode CLI on PATH and reads its version from the npm package file beside it. No program is started.")
                    : new SetupCheckDialog(Palette, () => SetupCheck.Run(SetupCheck.DefaultClaudeDir, SetupCheck.DefaultClaudeJsonPath));
        dialog.Owner = this;
        dialog.ShowDialog();
    }

    /// <summary>
    /// The opencode rows for <paramref name="shimPath"/> (null when no opencode program is on PATH). <paramref name="readFile"/> reads the package file beside
    /// the shim (OpenCodeSetupCheck passes its path) and returns null when it cannot be read. Nothing is started.
    /// </summary>
    internal static IReadOnlyList<SetupItem> OpenCodeRows(string? shimPath, Func<string, string?> readFile) =>
        OpenCodeSetupCheck.ForFound(shimPath, readFile);

    /// <summary>The text of a file the setup check reads, or null when it cannot be read (the row then shows the file as not read). The path is not logged.</summary>
    internal static string? ReadPackageText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
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
