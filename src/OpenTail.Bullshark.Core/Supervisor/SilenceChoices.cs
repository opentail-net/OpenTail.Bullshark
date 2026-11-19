using System.Text.Json.Nodes;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>One choice of the silent-working threshold on the Settings page: its minutes, its label, and whether it is not one of the presets.</summary>
public sealed record SilenceChoice(int Minutes, string Label, bool IsCustom);

/// <summary>
/// The silent-working threshold as the Settings page shows it (lifecycle batch L3, Naming). The file holds <c>silentMinutes</c>, and
/// <see cref="SilenceCheck.ThresholdOf"/> reads it: this class only maps what it reads to the page's choices, as <see cref="EscalationChoices"/> does.
/// A value that is not a preset (for example 20, or a number the reader clamps) shows as "Custom (N min)", with the minutes the engine uses. It never
/// writes the file. Pure.
/// </summary>
public static class SilenceChoices
{
    /// <summary>The presets in the page's order: Off, then 30, 45 (the default), 60 and 120 minutes.</summary>
    public static readonly IReadOnlyList<int> Presets = [0, 30, 45, 60, 120];

    /// <summary>The minutes the engine uses for a stored value: a missing or unusable value is the default, zero is off.</summary>
    public static int MinutesOf(JsonNode? stored) => (int)Math.Round(SilenceCheck.ThresholdOf(stored).TotalMinutes);

    /// <summary>The choice that a stored value shows on the page.</summary>
    public static SilenceChoice Of(JsonNode? stored)
    {
        var minutes = MinutesOf(stored);
        return Presets.Contains(minutes)
            ? new SilenceChoice(minutes, PresetLabel(minutes), IsCustom: false)
            : new SilenceChoice(minutes, CustomLabel(minutes), IsCustom: true);
    }

    /// <summary>The label of a preset: "Off", or the minutes.</summary>
    public static string PresetLabel(int minutes) => minutes == 0 ? "Off" : $"{minutes} min";

    /// <summary>The label of a value that is not a preset.</summary>
    public static string CustomLabel(int minutes) => $"Custom ({minutes} min)";
}
