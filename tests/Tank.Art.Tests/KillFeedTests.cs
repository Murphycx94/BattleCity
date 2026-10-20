using Tank.Client.Match;

namespace Tank.Art.Tests;

public class KillFeedTests
{
    private static void Kill(KillFeed feed, string victim, double now) =>
        feed.Add("甲", Side.Own, victim, Side.Enemy, friendly: false, now);

    [Fact]
    public void A_line_shows_for_five_seconds_and_then_goes()
    {
        var feed = new KillFeed();
        Kill(feed, "乙", now: 10);

        Assert.Equal(1, feed.Visible(10).Length);
        Assert.Equal(1, feed.Visible(14.99).Length);
        Assert.True(feed.Visible(15).IsEmpty);
    }

    [Fact]
    public void Only_the_five_newest_lines_show_and_the_oldest_goes_first()
    {
        var feed = new KillFeed();
        for (int i = 0; i < 7; i++) Kill(feed, $"敌{i}", now: i * 0.1);

        ReadOnlySpan<FeedLine> lines = feed.Visible(1);

        Assert.Equal(5, lines.Length);
        Assert.Equal("敌2", lines[0].Victim);   // oldest at the top
        Assert.Equal("敌6", lines[4].Victim);   // newest last
    }

    [Fact]
    public void Lines_expire_oldest_first_while_newer_ones_stay()
    {
        var feed = new KillFeed();
        Kill(feed, "早", now: 0);
        Kill(feed, "晚", now: 3);

        ReadOnlySpan<FeedLine> lines = feed.Visible(6);

        Assert.Equal(1, lines.Length);
        Assert.Equal("晚", lines[0].Victim);
    }

    [Fact]
    public void An_unknown_shooter_reads_victim_was_destroyed()
    {
        var feed = new KillFeed();
        feed.Add(null, Side.Enemy, "机器人 2", Side.Enemy, friendly: false, now: 0);
        Assert.Equal("机器人 2 被击毁", feed.Visible(0)[0].Text);
    }

    [Fact]
    public void A_kill_within_a_side_reads_friendly_fire()
    {
        var feed = new KillFeed();
        feed.Add("阿成", Side.Own, "小林", Side.Ally, friendly: true, now: 0);
        Assert.Equal("阿成 误伤 小林", feed.Visible(0)[0].Text);
    }

    [Fact]
    public void An_ordinary_kill_reads_killer_destroyed_victim()
    {
        var feed = new KillFeed();
        Kill(feed, "老周", now: 0);
        Assert.Equal("甲 击毁 老周", feed.Visible(0)[0].Text);
    }

    [Fact]
    public void The_revision_moves_when_a_line_arrives_or_expires_and_not_otherwise()
    {
        var feed = new KillFeed();
        int start = feed.Revision;
        feed.Visible(0);
        Assert.Equal(start, feed.Revision);

        Kill(feed, "乙", now: 0);
        int added = feed.Revision;
        Assert.NotEqual(start, added);
        feed.Visible(1);
        Assert.Equal(added, feed.Revision);

        feed.Visible(6);
        Assert.NotEqual(added, feed.Revision);
    }

    [Theory]
    [InlineData(Side.Own, Side.Ally, true)]
    [InlineData(Side.Ally, Side.Own, true)]
    [InlineData(Side.Own, Side.Enemy, false)]
    [InlineData(Side.Enemy, Side.Own, false)]
    [InlineData(Side.Enemy, Side.Enemy, false)]
    public void Friendly_means_neither_side_is_the_enemy(Side killer, Side victim, bool friendly) =>
        Assert.Equal(friendly, KillFeed.IsFriendly(killer, victim));
}
