using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Shoko.Plugin.TvMaze.Client;

namespace Shoko.Plugin.TvMaze.Tests;

/// <summary>
/// A <see cref="TvMazeClient"/> wired to captured responses instead of the
/// live API: requests are matched on their relative URI and answered with a
/// fixture, and every request is recorded so a test can assert how many calls
/// a refresh actually cost.
/// </summary>
public sealed class StubTvMazeApi : IDisposable
{
    private readonly Dictionary<string, string> _responses = [];
    private readonly StubHandler _handler;
    private readonly HttpClient _http;
    private readonly TvMazeRateLimiter _rateLimiter;

    /// <summary>
    /// Every relative URI requested, in order.
    /// </summary>
    public IReadOnlyList<string> Requests => _handler.Requests;

    /// <summary>
    /// The client under test, talking to the stubbed responses.
    /// </summary>
    public TvMazeClient Client { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StubTvMazeApi"/> class.
    /// </summary>
    /// <param name="abortWhen">
    /// Optional. Called with each relative URI and everything requested so
    /// far; answering <c>true</c> aborts that request, the way a sweep's
    /// deadline aborts the request in flight.
    /// </param>
    public StubTvMazeApi(Func<string, IReadOnlyList<string>, bool>? abortWhen = null)
    {
        _handler = new StubHandler(_responses, abortWhen);
        _http = new HttpClient(_handler) { BaseAddress = new Uri("https://api.tvmaze.com/") };
        _rateLimiter = new TvMazeRateLimiter();
        Client = new TvMazeClient(_http, _rateLimiter, NullLogger<TvMazeClient>.Instance);
    }

    /// <summary>
    /// Answers the lookup for a TheTVDB ID with a captured show response.
    /// </summary>
    /// <param name="tvdbShowId">The TheTVDB show ID.</param>
    /// <param name="fixtureName">The show fixture to answer with.</param>
    /// <returns>This instance, for chaining.</returns>
    public StubTvMazeApi WithShow(int tvdbShowId, string fixtureName)
    {
        _responses[$"/lookup/shows?thetvdb={tvdbShowId}"] = TvMazeFixtures.Read(fixtureName);
        return this;
    }

    /// <summary>
    /// Answers the episode list of a TVmaze show with a captured response.
    /// </summary>
    /// <param name="tvMazeShowId">The TVmaze show ID.</param>
    /// <param name="fixtureName">The episode list fixture to answer with.</param>
    /// <returns>This instance, for chaining.</returns>
    public StubTvMazeApi WithEpisodes(int tvMazeShowId, string fixtureName)
    {
        _responses[$"/shows/{tvMazeShowId}/episodes"] = TvMazeFixtures.Read(fixtureName);
        return this;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
        _rateLimiter.Dispose();
    }

    private sealed class StubHandler(IReadOnlyDictionary<string, string> responses, Func<string, IReadOnlyList<string>, bool>? abortWhen) : HttpMessageHandler
    {
        private readonly List<string> _requests = [];

        public IReadOnlyList<string> Requests => _requests;

        /// <inheritdoc/>
        /// <exception cref="OperationCanceledException">The request was aborted.</exception>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            lock (_requests)
                _requests.Add(path);

            if (abortWhen is not null && abortWhen(path, _requests))
                throw new OperationCanceledException(cancellationToken);

            if (!responses.TryGetValue(path, out var body))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
