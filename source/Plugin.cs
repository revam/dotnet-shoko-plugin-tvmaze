using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Shoko.Abstractions.Plugin;
using Shoko.Plugin.TvMaze.Client;

namespace Shoko.Plugin.TvMaze;

/// <summary>
/// Plugin providing an <c>IAiringScheduleProvider</c> that fills in broadcast
/// and streaming airing times for TMDB-linked shows from
/// <see href="https://www.tvmaze.com"/>, keyed through a show's TheTVDB ID.
/// </summary>
public class Plugin : IPlugin, IPluginServiceRegistration
{
    /// <inheritdoc/>
    public Guid ID { get; private init; } = new("5c09a10b-b36f-4a04-93db-6b085a0aafc2");

    /// <inheritdoc/>
    public string Name { get; private set; } = "TVmaze Airing Schedule";

    /// <inheritdoc/>
    public string Description { get; private set; } = """
        Fills in broadcast and streaming airing schedules for TMDB-linked shows from TVmaze,
        keyed through the TVDB ID.
    """;

    /// <inheritdoc/>
    public static void RegisterServices(IServiceCollection serviceCollection, IApplicationPaths applicationPaths)
    {
        // The provider itself is not registered: the server finds it by
        // reflection, constructs it with these services, and sweeps that very
        // instance, so nothing here needs a reference to it.
        serviceCollection.AddSingleton<TvMazeRateLimiter>();

        serviceCollection
            .AddHttpClient<TvMazeClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.tvmaze.com/");
                // TVmaze's data is CC BY-SA licensed: attribution is required,
                // and naming this plugin (with a link back) in the User-Agent
                // is the least intrusive place to carry that.
                client.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue("Shoko.Plugin.TvMaze", typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "1.0.0")
                );
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/revam/dotnet-shoko-plugin-tvmaze)"));
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .UseSocketsHttpHandler((handler, _) =>
            {
                handler.PooledConnectionLifetime = TimeSpan.FromMinutes(5);
                handler.PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2);
            });
    }
}
