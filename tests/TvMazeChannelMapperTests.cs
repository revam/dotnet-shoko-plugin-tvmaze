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
    public void A_network_with_a_country_becomes_a_television_channel_in_that_country()
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
        Assert.Equal("TV Tokyo", channel.Name);
        Assert.Equal(AiringChannelType.Television, channel.Type);
        Assert.Equal("JP", channel.CountryCode);
        Assert.Equal("JP", channel.ChannelCountryCode);
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
    public void A_global_web_channel_becomes_a_streaming_channel_without_a_country()
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

        Assert.Equal("Crunchyroll", channel.Name);
        Assert.Equal(AiringChannelType.Streaming, channel.Type);
        Assert.Equal("US", channel.CountryCode);
        Assert.Null(channel.ChannelCountryCode);
    }

    [Fact]
    public void A_regional_web_channel_keeps_its_country()
    {
        var show = new TvMazeShow
        {
            Id = 1,
            Name = "Streamed Thing",
            WebChannel = new TvMazeNetwork
            {
                Name = "ABEMA",
                Country = new TvMazeCountry { Code = "jp" },
            },
        };

        var channel = Assert.Single(TvMazeChannelMapper.Resolve(show));

        Assert.Equal("ABEMA", channel.Name);
        Assert.Equal("JP", channel.ChannelCountryCode);
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
        Assert.Contains(channels, c => c is { Type: AiringChannelType.Television, Name: "TV Tokyo", ChannelCountryCode: "JP" });
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

    [Fact]
    public void The_captured_one_piece_show_resolves_its_real_broadcast_network()
    {
        // Straight from /lookup/shows?thetvdb=81797.
        var show = TvMazeFixtures.Show("one-piece-show.json");

        var channel = Assert.Single(TvMazeChannelMapper.Resolve(show));

        Assert.Equal("Fuji TV", channel.Name);
        Assert.Equal(AiringChannelType.Television, channel.Type);
        Assert.Equal("JP", channel.CountryCode);
        Assert.Equal("Asia/Tokyo", channel.TimeZoneId);
    }

    [Fact]
    public void The_captured_streaming_only_show_resolves_a_countryless_web_channel()
    {
        // Netflix is worldwide, so TVmaze gives the web channel no country.
        var show = TvMazeFixtures.Show("edgerunners-show.json");

        var channel = Assert.Single(TvMazeChannelMapper.Resolve(show));

        Assert.Equal("Netflix", channel.Name);
        Assert.Equal(AiringChannelType.Streaming, channel.Type);
        Assert.Null(channel.CountryCode);
        Assert.Null(channel.TimeZoneId);
    }
}
