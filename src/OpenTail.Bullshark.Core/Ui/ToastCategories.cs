using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Ui;

/// <summary>
/// The kinds of toast the owner can switch off (toasts by event, 2026-11-18). Each one is a toast only: the sound and the flash for a wait are the
/// same for every kind. <see cref="Blocked"/>, <see cref="Problems"/> and <see cref="Notices"/> are the waits and notices of the Needs-you list;
/// <see cref="Ready"/> and <see cref="LimitReset"/> are silent toasts for a coder that is ready for review and a usage limit that reset.
/// </summary>
public enum ToastCategory
{
    Blocked,
    Problems,
    Notices,
    Ready,
    LimitReset,
}

/// <summary>
/// The per-kind switches of the toasts, kept in ui.json under <c>toastEvents</c>: one object with a boolean for each kind, by its name. A missing
/// object, a missing key or a value that is not a boolean means on, the same default as <c>notify</c>. Pure.
/// </summary>
public static class ToastCategories
{
    /// <summary>The ui.json key of the object.</summary>
    public const string Key = "toastEvents";

    /// <summary>The category of a reason of the Needs-you list. A reason not listed here is a notice, since it tells the owner and waits for nothing.</summary>
    public static ToastCategory Of(AttentionReason reason) => reason switch
    {
        AttentionReason.Blocked => ToastCategory.Blocked,
        AttentionReason.RestartLoop or AttentionReason.StartFailed or AttentionReason.Stopped or AttentionReason.Failed or AttentionReason.NameTaken
            => ToastCategory.Problems,
        // Overlap, Conflict, LimitReached and Silent are notices: Silent (lifecycle batch L3) is one too, since it may be a long command.
        _ => ToastCategory.Notices,
    };

    /// <summary>The key of a category in the object, as the file holds it.</summary>
    public static string NameOf(ToastCategory category) => category switch
    {
        ToastCategory.Blocked => "blocked",
        ToastCategory.Problems => "problems",
        ToastCategory.Notices => "notices",
        ToastCategory.Ready => "ready",
        ToastCategory.LimitReset => "limitReset",
        _ => category.ToString(),
    };

    /// <summary>The name a person reads for a category, in the Settings page and the status line.</summary>
    public static string TitleOf(ToastCategory category) => category switch
    {
        ToastCategory.Blocked => "Blocked",
        ToastCategory.Problems => "Problems",
        ToastCategory.Notices => "Notices",
        ToastCategory.Ready => "Ready for review",
        ToastCategory.LimitReset => "Usage limit reset",
        _ => category.ToString(),
    };

    /// <summary>Whether a category's toasts are on, from the stored object.</summary>
    public static bool Enabled(JsonNode? toastEvents, ToastCategory category) =>
        toastEvents is JsonObject obj && obj[NameOf(category)] is JsonValue json && json.TryGetValue<bool>(out var on) ? on : true;

    /// <summary>
    /// The object as it should be written after one switch changed: a copy of the stored object with that one key set, so the other keys and any
    /// key this version does not know are kept. The stored node is not changed (a JSON node has one parent).
    /// </summary>
    public static JsonObject With(JsonNode? toastEvents, ToastCategory category, bool on)
    {
        var copy = toastEvents is JsonObject existing ? (JsonObject)existing.DeepClone() : new JsonObject();
        copy[NameOf(category)] = JsonValue.Create(on);
        return copy;
    }
}
