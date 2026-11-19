using System.Windows;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>The logical tree below an element, depth first. The views' controls are logical children whether or not a window shows them.</summary>
internal static class LogicalTree
{
    public static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Walk(child)) yield return found;
    }
}
