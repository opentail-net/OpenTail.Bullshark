using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The model and effort rows of a resume screen, and its cost line, for a start in a platform other than the screen's own (resume source and target plan,
/// S4, RS-D12). A cross start runs at the target's own default model and effort: the rows do not apply to it, and the cost line names no model.
/// Pure: no window is made, so the rule and the wording are tested without one.
/// </summary>
public static class CrossModelRow
{
    /// <summary>True when the model and effort rows apply: the start is in the screen's own platform, not in another one.</summary>
    public static bool ShowsModel(ProviderId own, ProviderId target) => target == own;

    /// <summary>The cost line of a cross start: the new session starts at the target's own default model and effort (RS-D12).</summary>
    public static string CostLineForCross(ProviderId target) =>
        "The new " + ContinueDecision.NameOf(target) + " session starts at its own default model and effort.";
}
