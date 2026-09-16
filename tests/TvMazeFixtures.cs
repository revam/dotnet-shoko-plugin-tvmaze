using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Shoko.Plugin.TvMaze.Client.Models;

namespace Shoko.Plugin.TvMaze.Tests;

/// <summary>
/// Real TVmaze responses, captured from the public API on 2026-09-16 and
/// trimmed of the fields this plugin never reads (summary, images,
/// <c>_links</c>, per-episode ratings). Nothing here is hand-written: the
/// quirks they carry — One Piece numbering its seasons by year, Frieren's
/// late-night slot whose <c>airdate</c> is a day behind its <c>airstamp</c>,
/// Netflix having no country — are the ones the live API actually serves.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><c>frieren-show.json</c> — <c>/lookup/shows?thetvdb=424536</c></item>
///   <item><c>frieren-episodes.json</c> — <c>/shows/69956/episodes</c>, seasons 1 and 2, three episodes each</item>
///   <item><c>one-piece-show.json</c> — <c>/lookup/shows?thetvdb=81797</c></item>
///   <item><c>one-piece-episodes.json</c> — <c>/shows/1505/episodes</c>, the first three</item>
///   <item><c>edgerunners-show.json</c> — <c>/singlesearch/shows?q=cyberpunk+edgerunners</c></item>
/// </list>
/// </remarks>
public static class TvMazeFixtures
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// The raw JSON of one captured response.
    /// </summary>
    /// <param name="name">The fixture's file name, e.g. <c>frieren-show.json</c>.</param>
    /// <exception cref="FileNotFoundException">No such fixture is embedded.</exception>
    /// <returns>The fixture's contents.</returns>
    public static string Read(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"Shoko.Plugin.TvMaze.Tests.Fixtures.{name}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"No embedded TVmaze fixture named \"{resourceName}\".", name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Deserializes a captured show response the way the client does.
    /// </summary>
    /// <param name="name">The fixture's file name.</param>
    /// <returns>The show.</returns>
    public static TvMazeShow Show(string name)
        => JsonSerializer.Deserialize<TvMazeShow>(Read(name), JsonOptions)!;

    /// <summary>
    /// Deserializes a captured episode list response the way the client does.
    /// </summary>
    /// <param name="name">The fixture's file name.</param>
    /// <returns>The episodes.</returns>
    public static IReadOnlyList<TvMazeEpisode> Episodes(string name)
        => JsonSerializer.Deserialize<List<TvMazeEpisode>>(Read(name), JsonOptions)!;
}
