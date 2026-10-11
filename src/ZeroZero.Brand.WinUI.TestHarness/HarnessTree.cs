using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// How the rig reaches into a realised tree: a search by condition, and a press through the
/// automation peer so the control's own click path runs. The controls keep their parts private, so
/// the rig walks to them the way a screen reader would.
/// </summary>
internal static class HarnessTree
{
    /// <summary>Every node under <paramref name="root"/> and the root itself, parents before children.</summary>
    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            foreach (var node in Descendants(VisualTreeHelper.GetChild(root, i))) yield return node;
        }
    }

    /// <summary>The first node of type <typeparamref name="T"/> that satisfies <paramref name="match"/>
    /// (any, when none is given).</summary>
    public static T? Find<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject =>
        Descendants(root).OfType<T>().FirstOrDefault(node => match is null || match(node));

    /// <summary>The first element of type <typeparamref name="T"/> carrying this name in the markup.</summary>
    public static T? FindNamed<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Find<T>(root, element => element.Name == name);

    /// <summary>
    /// Presses the first button satisfying <paramref name="match"/>. False when there is no such
    /// button or it offers no invoke pattern, so a caller can report the miss instead of waiting for
    /// an answer that will never come.
    /// </summary>
    public static bool Press(DependencyObject root, Func<Button, bool> match) =>
        Find(root, match) is { } button && Press(button);

    /// <summary>Presses a button through its automation peer; false when the peer cannot invoke.</summary>
    public static bool Press(Button button)
    {
        if (FrameworkElementAutomationPeer.CreatePeerForElement(button) is not IInvokeProvider invoke) return false;
        invoke.Invoke();
        return true;
    }
}
