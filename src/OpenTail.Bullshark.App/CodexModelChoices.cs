using System.Windows.Controls;
using OpenTail.Bullshark.Core.Codex;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The model dropdown of a new Codex session (P9, decisions M-D2 to M-D4). The entries are the models the account is offered (<see cref="CodexModelList"/>),
/// with the default preselected; its id is what <c>-m</c> takes. When the list could not be read, or has no model, the only entry is "Codex's default",
/// which passes no model, so Codex uses its own configured default. No model name is written in the product.
/// </summary>
internal static class CodexModelChoices
{
    /// <summary>The one entry shown when no model is offered. Its item carries no model id.</summary>
    public const string FallbackLabel = "Codex's default";

    /// <summary>
    /// Fills <paramref name="box"/> from <paramref name="reading"/> and preselects the default. A null reading (no list was asked for) gives the fallback
    /// entry. Returns the note for the box: what the list is, or why Codex's default is used. Does not change whether the box is enabled.
    /// </summary>
    public static string Fill(ComboBox box, CodexModelReading? reading)
    {
        box.Items.Clear();
        if (reading is { Problem: CodexModelProblem.None } read)
        {
            var defaultIndex = 0;
            foreach (var model in read.Models)
            {
                box.Items.Add(new ComboBoxItem { Content = Label(model), Tag = model.Id, ToolTip = model.Id });
                if (model.IsDefault) defaultIndex = box.Items.Count - 1;
            }
            box.SelectedIndex = defaultIndex;
            return "The models this account is offered. The default is selected.";
        }

        box.Items.Add(new ComboBoxItem { Content = FallbackLabel });
        box.SelectedIndex = 0;
        return reading is null
            ? "Codex's configured default is used."
            : $"The model list could not be read ({reading.Problem}). Codex's configured default is used.";
    }

    /// <summary>The model id of the selected entry, or null for Codex's default (the fallback entry, which has no id).</summary>
    public static string? SelectedModel(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    /// <summary>The entry's text: the display name, the id when it differs, and "default" for the default model.</summary>
    private static string Label(CodexModelEntry model)
    {
        var name = model.DisplayName == model.Id ? model.Id : $"{model.DisplayName} ({model.Id})";
        return model.IsDefault ? name + " - default" : name;
    }
}
