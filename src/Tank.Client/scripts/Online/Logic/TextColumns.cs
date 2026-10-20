namespace Tank.Client.Online;

/// <summary>Column arithmetic for the monospace tables (room list, scoreboard); engine-neutral twin of <c>UiKit.Pad</c>.</summary>
public static class TextColumns
{
    /// <summary>How many monospace columns <paramref name="text"/> takes: CJK and full-width characters take two.</summary>
    public static int Width(string text)
    {
        int width = 0;
        foreach (char c in text) width += IsWide(c) ? 2 : 1;
        return width;
    }

    /// <summary>
    /// Right-pads <paramref name="text"/> to <paramref name="columns"/> columns. <see cref="string.PadRight(int)"/> alone would
    /// misalign rows that mix wide characters with ASCII. Text that is already wider is returned as it is.
    /// </summary>
    public static string PadColumns(string text, int columns)
    {
        int width = Width(text);
        return width >= columns ? text : text + new string(' ', columns - width);
    }

    private static bool IsWide(char c) =>
        c is >= 'ᄀ' and <= 'ᅟ' or >= '⺀' and <= '꓏' or >= '가' and <= '힣'
            or >= '豈' and <= '﫿' or >= '︰' and <= '﹏' or >= '＀' and <= '｠' or >= '￠' and <= '￦';
}
