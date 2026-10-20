using System.Text;
using Tank.Net.Battle;

namespace Tank.Client.UI;

/// <summary>
/// The player name's wire limit, applied where the name is typed: <see cref="BattleProtocol.MaxNameBytes"/> bytes of
/// UTF-8, not characters (<c>BitWriter.WriteString</c> cuts a longer name silently, so ten 汉字 would reach the room as
/// eight). Cutting here makes the box show what the room will.
/// </summary>
public static class NameLimit
{
    /// <summary><paramref name="text"/> cut to at most <paramref name="maxBytes"/> UTF-8 bytes, whole characters only (a surrogate pair stays together).</summary>
    public static string Clip(string text, int maxBytes = BattleProtocol.MaxNameBytes)
    {
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes) return text;
        int bytes = 0, end = 0;
        while (end < text.Length)
        {
            int width = char.IsSurrogatePair(text, end) ? 2 : 1;
            int size = Encoding.UTF8.GetByteCount(text.AsSpan(end, width));
            if (bytes + size > maxBytes) break;
            bytes += size;
            end += width;
        }
        return text[..end];
    }
}
