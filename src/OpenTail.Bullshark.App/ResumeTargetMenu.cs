using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The platform items of the Start menu of the three resume screens (resume source and target plan, RS-D2, RS-D3, RS-D8): one checkable item per
/// platform, in the order Claude, Codex, Copilot. The checked item is the pick. An item is on only in a summary, and only where
/// <see cref="ResumeTarget.Allowed"/> says so for a summary; a disabled item carries <see cref="ResumeTarget.RefusalReason"/> as its tooltip. The screens
/// keep the pick; this sets the items, and reads the platform a click names.
/// </summary>
public static class ResumeTargetMenu
{
    /// <summary>
    /// Sets one item for <paramref name="platform"/>: the platform it names (its tag), its header, whether it is the pick, and whether it may be picked now.
    /// </summary>
    /// <param name="menuOn">True when the menu may be used now: a summary, with nothing being made (RS-D2).</param>
    public static void Apply(MenuItem item, ProviderId platform, ProviderId source, ProviderId selected, bool menuOn)
    {
        item.Tag = platform;
        item.Header = ResumeTarget.ButtonOf(platform);
        item.IsCheckable = true;
        item.IsChecked = platform == selected;
        var allowed = ResumeTarget.Allowed(source, ContinueMode.Summary, platform);
        item.IsEnabled = menuOn && allowed;
        item.ToolTip = allowed ? null : ResumeTarget.RefusalReason(source, ContinueMode.Summary, platform);
        ToolTipService.SetShowOnDisabled(item, true);
    }

    /// <summary>The platform a menu item names, or null when the sender is not a platform item.</summary>
    public static ProviderId? PlatformOf(object sender) => sender is MenuItem { Tag: ProviderId platform } ? platform : null;
}
