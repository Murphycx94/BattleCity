using System.Text;
using Tank.Client.UI;
using Tank.Net.Battle;

namespace Tank.Art.Tests;

public class NameLimitTests
{
    [Theory]
    [InlineData("")]
    [InlineData("阿成")]
    [InlineData("Sergeant_Wolfgang_Becker")]   // 24 ASCII bytes: exactly the limit
    [InlineData("一二三四五六七八")]               // 8 × 3 bytes: exactly the limit
    public void A_name_within_the_limit_is_kept_as_it_is(string name)
    {
        Assert.Equal(name, NameLimit.Clip(name));
    }

    [Fact]
    public void Ten_han_characters_are_cut_to_the_eight_that_fit_in_24_bytes()
    {
        Assert.Equal("一二三四五六七八", NameLimit.Clip("一二三四五六七八九十"));
    }

    [Fact]
    public void A_long_ascii_name_is_cut_to_24_characters()
    {
        Assert.Equal(new string('x', BattleProtocol.MaxNameBytes), NameLimit.Clip(new string('x', 30)));
    }

    [Fact]
    public void A_character_that_would_straddle_the_limit_is_left_out_whole()
    {
        // 22 ASCII bytes leave 2: a 3-byte 汉字 doesn't fit, so the cut falls before it, not inside it.
        string name = new string('a', 22) + "坦克";
        Assert.Equal(new string('a', 22), NameLimit.Clip(name));
    }

    [Fact]
    public void A_surrogate_pair_is_never_split()
    {
        string name = new string('a', 21) + "🚀b";   // the rocket is 4 bytes: 21 + 4 > 24
        string clipped = NameLimit.Clip(name);
        Assert.Equal(new string('a', 21), clipped);
        Assert.True(Encoding.UTF8.GetByteCount(clipped) <= BattleProtocol.MaxNameBytes);
    }
}
