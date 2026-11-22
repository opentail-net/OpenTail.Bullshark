using System.Linq;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The target chooser of the three resume screens (resume source and target plan, S3b-B2, RS-D3, RS-D8): a ComboBox with the three platforms. Each item is
/// enabled as <see cref="ResumeTarget.Allowed"/> says for a summary; a disabled item carries <see cref="ResumeTarget.RefusalReason"/> as its tooltip. The
/// source's own platform is the default. The screens read the owner's pick with <see cref="SelectedOf"/>.
/// </summary>
public static class ResumeTargetBox
{
    private static readonly ProviderId[] Platforms = { ProviderId.Claude, ProviderId.Codex, ProviderId.Copilot };

    /// <summary>Fills <paramref name="box"/> with the three platforms, and selects <paramref name="source"/>, the default (RS-D3).</summary>
    public static void Fill(ComboBox box, ProviderId source)
    {
        box.Items.Clear();
        foreach (var platform in Platforms)
        {
            var item = new ComboBoxItem
            {
                Content = ContinueDecision.NameOf(platform),
                Tag = platform,
                IsEnabled = ResumeTarget.Allowed(source, ContinueMode.Summary, platform),
                ToolTip = ResumeTarget.RefusalReason(source, ContinueMode.Summary, platform),
            };
            ToolTipService.SetShowOnDisabled(item, true);
            box.Items.Add(item);
        }
        Select(box, ResumeTarget.DefaultTarget(source));
    }

    /// <summary>The platform the box shows, or <paramref name="source"/> when no item is selected.</summary>
    public static ProviderId SelectedOf(ComboBox box, ProviderId source) =>
        box.SelectedItem is ComboBoxItem { Tag: ProviderId platform } ? platform : source;

    /// <summary>Selects <paramref name="platform"/> in the box. A platform the box does not list is not selected.</summary>
    public static void Select(ComboBox box, ProviderId platform)
    {
        var item = box.Items.OfType<ComboBoxItem>().FirstOrDefault(candidate => candidate.Tag is ProviderId tag && tag == platform);
        if (item is not null) box.SelectedItem = item;
    }
}
