using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shoko.Plugin.TvMaze.Client.Models;

namespace Shoko.Plugin.TvMaze.Client;

/// <summary>
/// Thin wrapper around the public TVmaze API (<see href="https://api.tvmaze.com"/>),
/// respecting TVmaze's documented rate limit through <see cref="TvMazeRateLimiter"/>.
/// No API key is required or supported: the endpoints used here are the free,
/// unauthenticated ones.
/// </summary>
public sealed class TvMazeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly TvMazeRateLimiter _rateLimiter;
    private readonly ILogger<TvMazeClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TvMazeClient"/> class.
    /// </summary>
    /// <param name="http">
    /// The <see cref="HttpClient"/> injected via the HTTP client factory,
    /// pre-configured with TVmaze's base address and a User-Agent header.
    /// </param>
    /// <param name="rateLimiter">The shared rate limiter for TVmaze calls.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public TvMazeClient(HttpClient http, TvMazeRateLimiter rateLimiter, ILogger<TvMazeClient> logger)
    {
        _http = http;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    /// <summary>
    /// Looks up a show by its TheTVDB ID, following TVmaze's redirect to the
    /// matching show.
    /// </summary>
    /// <param name="tvdbShowId">The TheTVDB show ID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching show, or <c>null</c> when TVmaze has none.</returns>
    public async Task<TvMazeShow?> LookupShowByTvdbIdAsync(int tvdbShowId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync($"lookup/shows?thetvdb={tvdbShowId}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TvMazeShow>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the full episode list for a show, in airing order. Specials are
    /// excluded, matching TVmaze's default for this endpoint.
    /// </summary>
    /// <param name="tvMazeShowId">The TVmaze internal show ID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The show's episodes, or an empty list when TVmaze has none.</returns>
    public async Task<IReadOnlyList<TvMazeEpisode>> GetEpisodesAsync(int tvMazeShowId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync($"shows/{tvMazeShowId}/episodes", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return [];

        response.EnsureSuccessStatusCode();
        var episodes = await response.Content.ReadFromJsonAsync<List<TvMazeEpisode>>(JsonOptions, cancellationToken).ConfigureAwait(false);
        return episodes ?? [];
    }

    private async Task<HttpResponseMessage> SendAsync(string requestUri, CancellationToken cancellationToken)
    {
        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var response = await _http.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                _logger.LogWarning("TVmaze returned 429 (Too Many Requests) for {RequestUri}; backing off.", requestUri);

            return response;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }
}
