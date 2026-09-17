# Shoko TVmaze Airing Schedule Plugin

A [Shoko](https://shokoanime.com/) plugin that fills in broadcast and
streaming airing schedules for TMDB-linked shows from
[TVmaze](https://www.tvmaze.com/), through Shoko's `IAiringScheduleService`.

TMDB shows, seasons and episodes are core entities in that service, so this
plugin needs no entity resolver of its own: once it writes a schedule, any
shoko series linked to the TMDB show picks it up automatically through its
existing links.

## How it works

- **What a refresh accepts**: a refresh arrives for whatever entity it was
  requested for, which for an ordinary series refresh is the *shoko series*,
  not a TMDB show. So the provider resolves its own way to TMDB: a TMDB show
  is used as-is, a shoko series resolves to every TMDB show it is linked to,
  and any other series (an AniDB anime, an AniList anime) goes through its
  shoko series to reach the same TMDB shows. Every dead end along the way is
  logged at Debug — a refresh that silently does nothing is indistinguishable
  from a broken provider.
- **Several linked shows**: one anime can be linked to several TMDB shows (a
  split-cour run is usually one TMDB show per cour), and all of them are
  refreshed, not just the first. They are grouped by `TvdbShowID` first, so
  two TMDB shows keyed to the same TheTVDB show cost one lookup and one
  episode list between them, and each still gets its own schedules written
  against its own seasons and episodes. The refresh counts as work done when
  any one of them produced a schedule.
- **Keying**: TVmaze has no AniDB or TMDB IDs of its own. Instead, a TMDB
  show's `TvdbShowID` is looked up against
  `GET https://api.tvmaze.com/lookup/shows?thetvdb={id}`, which redirects
  (HTTP 301) to the matching show, or answers 404 when TVmaze has none. A
  show with no `TvdbShowID` at all is skipped outright: this provider simply
  has nothing to key it on.
- **Episodes**: `GET /shows/{id}/episodes` returns the full episode list, each
  carrying a precise `airstamp` (an ISO timestamp with an offset), which is
  the only time field used. The sibling `airdate`/`airtime` pair is the
  *broadcast day* and local clock time, and for a late-night slot the two
  disagree by a day — Frieren's 01:00 JST slot is listed under the previous
  day's `airdate` — so times are taken from the `airstamp` and submitted to
  Shoko in UTC. TVmaze's season and episode numbers are
  matched against the TMDB season with the same season number, and against
  that season's episodes by episode number. An episode that cannot be matched
  this way — because TVmaze and TMDB disagree on the numbering, most often —
  is skipped rather than guessed at.
- **Schedules**: one schedule per (TMDB season, channel), with a single
  `Original` track. TVmaze doesn't distinguish a dub or a subtitled release
  from the original broadcast, so that is the only kind this provider
  declares. Coverage (`FirstEpisodeNumber`/`LastEpisodeNumber`) comes from the
  TMDB season's own episode count.
- **Channels**: the show's `network` becomes a Television channel, its
  `webChannel` becomes a Streaming channel — a show can have either, both (a
  simulcast on a streaming service alongside its broadcast run), or neither.
  When TVmaze names a country for the network or web channel, the channel is
  registered under its regional name (`IAiringScheduleService.GetRegionalChannelName`,
  e.g. `TV Tokyo (JP)`), so multiple providers naming the same regional
  service converge on one channel. The channel's time zone comes from
  `network.country.timezone`.
- **Finished shows**: TVmaze reports one run `status` for the whole show, not
  per season, so whether a given season's schedule is "finished" is inferred:
  every season below the show's latest is necessarily done, and the latest
  one is left open only while the show's own status is `"Running"`.
- **Sweeps**: `RefreshAsync` answers one series because something asked for
  it (a user click, a newly linked show); walking every TMDB show plausibly
  keyed to TVmaze is the other half, and the provider opts into having the
  server do the driving by implementing `ISweepingAiringScheduleProvider`.
  The server decides when a sweep is due (this provider suggests daily) and
  how long one chunk may run; `SweepAsync` refreshes shows in ID order, stops
  as soon as its deadline fires, and hands back the TMDB show ID it got to as
  the cursor the next chunk resumes after.
- **Whole-line writes**: one request hands back a show's entire episode list,
  so a season's schedule is submitted through `SetAirings`, which reads a
  submission as that schedule's whole line and works out delays, pre-emptions
  and hiatuses across it.

## Rate limits and licensing

Per TVmaze's own API documentation (<https://www.tvmaze.com/api>, checked
while building this plugin):

- **Rate limit**: "API calls are rate limited to allow at least 20 calls
  every 10 seconds per IP address." Exceeding it can return HTTP 429; TVmaze
  recommends backing off. This plugin enforces that limit itself with a
  token bucket (`TvMazeRateLimiter`, 20 tokens refilling at 2/second — the
  same average, with a small burst allowance) shared by every request the
  plugin makes, so a large sweep simply takes longer rather than risking a
  429.
- **License**: "Use of the TVmaze API is licensed by CC BY-SA. This means
  the data can freely be used for any purpose, as long as TVmaze is properly
  credited as source," with the ShareAlike clause carrying forward.
  Attribution can be satisfied by linking back to TVmaze from the
  application. This plugin identifies itself to TVmaze, and credits TVmaze
  in turn, through its HTTP `User-Agent` header
  (`Shoko.Plugin.TvMaze/<version> (+<repository URL>)`); if you build a UI
  surface on top of the schedules this plugin writes, remember that the
  ShareAlike/attribution obligation is yours too, not just the plugin's.
- No API key is used or required. The premium, user-level API TVmaze also
  offers is unrelated to show/episode data and is not used here.

## Known limitations

- **Mid-run channel moves are not modeled precisely.** TVmaze's `/shows/{id}`
  shape only exposes a show's *current* network/web channel, not one per
  season, so a show that changed networks partway through its run has every
  season attributed to whichever channel TVmaze currently lists. When a
  show's channel does change between refreshes, this provider does not try
  to rewrite the old schedule's channel (the airing schedule service treats a
  channel change as a different schedule) — it simply starts writing to a
  new, differently-keyed schedule, leaving the old one stale rather than
  updated or removed.
- **Only `Original`.** No dub or subtitle distinction is made; see "How it
  works" above.
- **Season numbering has to agree with TMDB.** A long-running show that
  TVmaze numbers by broadcast year instead of by season — One Piece's TVmaze
  seasons run `1999`, `2000`, … — lines up with no TMDB season, so nothing is
  written for it. The mismatch is logged per season at Debug rather than
  guessed at.

## Configuration

| Setting | Default | Description |
|---|---|---|
| **Stop Sweeping Ended Shows After (days)** | `60` | How many days past a show's known end date the sweep keeps refreshing it, in case TVmaze corrects an air date after the fact. `0` sweeps ended shows forever. |

How often the sweep runs is the server's setting rather than the plugin's: the provider suggests once a day, and the interval actually used lives on the provider's own page in the Shoko UI, where anything under fifteen minutes is clamped.

## Installation

### GUI (Recommended)

1. Open the Shoko Web UI and navigate to **Settings → Plugins → Repositories**.
2. Add the manifest URL:
   ```
   https://raw.githubusercontent.com/revam/dotnet-shoko-plugin-tvmaze/metadata/manifest.json
   ```
3. Go to **Settings → Plugins → Browse** and find **TVmaze Airing Schedule**.
4. Click **Install** on the desired version.
5. Restart Shoko.

### Manual

1. Download the latest `Shoko.Plugin.TvMaze-<version>-any.zip` from the
   [Releases](../../releases) page.
2. Extract the ZIP and place `Shoko.Plugin.TvMaze.dll` into your Shoko
   **Plugins** folder.
3. Restart Shoko.

## Building from Source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet restore
dotnet build --configuration Release
```

The compiled assembly will be located at
`source/bin/Release/net10.0/Shoko.Plugin.TvMaze.dll`.

### Running the tests

```bash
dotnet test tests/Shoko.Plugin.TvMaze.Tests.csproj
```

Tests cover the pure mapping logic (matching TVmaze episodes to TMDB
episodes, resolving TVmaze networks/web channels to airing channels, and the
finished-season heuristic) and the rate limiter's token bucket, all without
needing a running Shoko server.

## License

This project is licensed under the MIT License. The data it fetches from
TVmaze is licensed separately, under CC BY-SA — see **Rate limits and
licensing** above.
