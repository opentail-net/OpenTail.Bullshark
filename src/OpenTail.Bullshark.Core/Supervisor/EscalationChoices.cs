using System.Text.Json.Nodes;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>One choice of the re-alert interval on the Settings page: its minutes, its label, and whether it is not one of the presets.</summary>
public sealed record EscalationChoice(int Minutes, string Label, bool IsCustom);

/// <summary>
/// The re-alert interval as the Settings page shows it (the Insights and Settings plan, Naming). The file holds <c>escalateMinutes</c>, and
/// <see cref="AttentionEscalation.IntervalOf"/> reads it: this class only maps what it reads to the page's choices. A value that is not one
/// of the presets (for example 20, or a number the reader clamps) shows as "Custom (N min)", with the minutes the engine uses. It never
/// writes the file. Pure.
/// </summary>
public static class EscalationChoices
{
    /// <summary>The presets in the page's order: Off, then 5, 15 (the default), 30 and 60 minutes.</summary>
    public static readonly IReadOnlyList<int> Presets = [0, 5, 15, 30, 60];

    /// <summary>The minutes the engine uses for a stored value: a missing or unusable value is the default, zero is off.</summary>
    public static int MinutesOf(JsonNode? stored) => (int)Math.Round(AttentionEscalation.IntervalOf(stored).TotalMinutes);

    /// <summary>The choice that a stored value shows on the page.</summary>
    public static EscalationChoice Of(JsonNode? stored)
    {
        var minutes = MinutesOf(stored);
        return Presets.Contains(minutes)
            ? new EscalationChoice(minutes, PresetLabel(minutes), IsCustom: false)
            : new EscalationChoice(minutes, CustomLabel(minutes), IsCustom: true);
    }

    /// <summary>The label of a preset: "Off", or the minutes.</summary>
    public static string PresetLabel(int minutes) => minutes == 0 ? "Off" : $"{minutes} min";

    /// <summary>The label of a value that is not a preset.</summary>
    public static string CustomLabel(int minutes) => $"Custom ({minutes} min)";
}
