using Shoko.Abstractions.Metadata.Airing;
using Shoko.Plugin.TvMaze.Client.Models;
using Shoko.Plugin.TvMaze.Mapping;
using Xunit;

namespace Shoko.Plugin.TvMaze.Tests;

/// <summary>
/// Tests for turning a TVmaze show's <c>network</c>/<c>webChannel</c> fields
/// into the channels the airing schedule service expects.
/// </summary>
public class TvMazeChannelMapperTests
{
    [Fact]
    public void A_show_with_neither_network_nor_web_channel_resolves_no_channels()
    {
        var show = new TvMazeShow { Id = 1, Name = "Nothing" };

        var channels = TvMazeChannelMapper.Resolve(show);

        Assert.Empty(channels);
    }

    [Fact]
    public void A_network_with_a_country_becomes_a_regional_television_channel()
    {
        var show = new TvMazeShow
        {
            Id = 1,
            Name = "Chainsaw Man",
            Network = new TvMazeNetwork
            {
                Name = "TV Tokyo",
                Country = new TvMazeCountry { Code = "JP", Timezone = "Asia/Tokyo" },
            },
        };

        var channels = TvMazeChannelMapper.Resolve(show);

        var channel = Assert.Single(channels);
        Assert.Equal("TV Tokyo (JP)", channel.Name);
        Assert.Equal(AiringChannelType.Television, channel.Type);
        Assert.Equal("JP", channel.CountryCode);
        Assert.Equal("Asia/Tokyo", channel.TimeZoneId);
    }

    [Fact]
    public void A_network_with_no_country_keeps_its_plain_name()
    {
        var show = new TvMazeShow
        {
            Id = 1,
            Name = "Worldwide Thing",
            Network = new TvMazeNetwork { Name = "Some Network" },
        };

        var channel = Assert.Single(TvMazeChannelMapper.Resolve(show));

        Assert.Equal("Some Network", channel.Name);
        Assert.Null(channel.CountryCode);
        Assert.Null(channel.TimeZoneId);
    }

    [Fact]
    public void A_web_channel_becomes_a_streaming_channel()
    {
        var show = new TvMazeShow
        {
            Id = 1,
            Name = "Streamed Thing",
            WebChannel = new TvMazeNetwork
            {
                Name = "Crunchyroll",
                Country = new TvMazeCountry { Code = "US" },
            },
        };

        var channel = Assert.Single(TvMazeChannelMapper.Resolve(show));

        Assert.Equal("Crunchyroll (US)", channel.Name);
        Assert.Equal(AiringChannelType.Streaming, channel.Type);
    }

    [Fact]
    public void A_show_airing_on_both_gets_two_channels()
    {
        var show = new TvMazeShow
        {
            Id = 1,
            Name = "Simulcast Thing",
            Network = new TvMazeNetwork { Name = "TV Tokyo", Country = new TvMazeCountry { Code = "JP" } },
            WebChannel = new TvMazeNetwork { Name = "Crunchyroll" },
        };

        var channels = TvMazeChannelMapper.Resolve(show);

        Assert.Equal(2, channels.Count);
        Assert.Contains(channels, c => c is { Type: AiringChannelType.Television, Name: "TV Tokyo (JP)" });
        Assert.Contains(channels, c => c is { Type: AiringChannelType.Streaming, Name: "Crunchyroll" });
    }

    [Fact]
    public void A_network_with_a_blank_name_is_ignored()
    {
        var show = new TvMazeShow
        {
            Id = 1,
            Name = "Malformed",
            Network = new TvMazeNetwork { Name = "  " },
        };

        Assert.Empty(TvMazeChannelMapper.Resolve(show));
    }
}
