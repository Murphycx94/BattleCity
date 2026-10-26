using Godot;

namespace Tank.Client.UI;

/// <summary>
/// Sets the window title, and logs it as "[window] …" when it changes: the log is how a run shows its title, since the
/// OS doesn't let another process read a window's title.
/// </summary>
public static class WindowTitle
{
    public static void Set(Node node, string title)
    {
        Window window = node.GetWindow();
        if (window.Title == title) return;
        window.Title = title;
        GD.Print($"[window] {title}");
    }
}
