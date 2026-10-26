using Tank.Client.Online;

namespace Tank.Art.Tests;

public class SeatTokensTests
{
    [Fact]
    public void A_room_never_joined_has_no_token()
    {
        Assert.Equal(0u, new SeatTokens().For("192.168.1.20", 27015));
    }

    [Fact]
    public void The_token_a_room_handed_out_is_given_back_for_that_room_only()
    {
        var tokens = new SeatTokens();
        tokens.Remember("192.168.1.20", 27015, 0xCAFE);
        tokens.Remember("192.168.1.21", 27015, 0xBEEF);

        Assert.Equal(0xCAFEu, tokens.For("192.168.1.20", 27015));
        Assert.Equal(0xBEEFu, tokens.For("192.168.1.21", 27015));
        Assert.Equal(0u, tokens.For("192.168.1.20", 27016));   // another room on the same machine
    }

    [Fact]
    public void A_typed_address_matches_whatever_its_case_and_surrounding_spaces()
    {
        var tokens = new SeatTokens();
        tokens.Remember("Mac-Mini.local", 27015, 7);

        Assert.Equal(7u, tokens.For(" mac-mini.LOCAL ", 27015));
    }

    [Fact]
    public void A_newer_token_replaces_the_old_and_no_token_forgets_nothing()
    {
        var tokens = new SeatTokens();
        tokens.Remember("10.0.0.2", 27015, 1);
        tokens.Remember("10.0.0.2", 27015, 2);
        tokens.Remember("10.0.0.2", 27015, 0);   // a session that never got a seat (rejected, or the room full)

        Assert.Equal(2u, tokens.For("10.0.0.2", 27015));
    }

    [Theory]
    [InlineData("localhost", "127.0.0.1")]
    [InlineData("LocalHost", "127.0.0.1")]
    [InlineData("::1", "127.0.0.1")]
    [InlineData("[::1]", "127.0.0.1")]
    [InlineData("[::1]", "localhost")]
    [InlineData("::ffff:192.168.1.20", "192.168.1.20")]
    [InlineData("[::FFFF:192.168.1.20]", "192.168.1.20")]
    [InlineData("::ffff:127.0.0.1", "localhost")]
    [InlineData("[fe80::1]", "FE80:0:0:0:0:0:0:1")]
    [InlineData(" 192.168.1.20 ", "192.168.1.20")]
    [InlineData("Mac-Mini.LOCAL", " mac-mini.local")]
    public void One_room_written_two_ways_is_one_key(string remembered, string asked)
    {
        var tokens = new SeatTokens();
        tokens.Remember(remembered, 27015, 0xCAFE);

        Assert.Equal(0xCAFEu, tokens.For(asked, 27015));
        Assert.Equal(0u, tokens.For(asked, 27016));   // the port still counts
    }

    [Theory]
    [InlineData("192.168.1.20", "127.0.0.1")]      // the same machine by another route: another key (no lookups)
    [InlineData("mac-mini.local", "192.168.1.20")]
    [InlineData("::2", "127.0.0.1")]
    [InlineData("192.168.1.2", "192.168.1.20")]
    public void Different_addresses_are_different_keys(string remembered, string asked)
    {
        var tokens = new SeatTokens();
        tokens.Remember(remembered, 27015, 0xCAFE);

        Assert.Equal(0u, tokens.For(asked, 27015));
    }
}
