using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Scry.Core;

namespace Scry.Client;

/// <summary>
/// Builds the DI service provider for the <c>scry</c> client.
/// </summary>
internal static class Bootstrap
{
    public static ServiceProvider Build(bool verbose)
    {
        var cfg = ScryConfig.Load();
        var resolved = ScryLogging.Resolve("scry", verbose, cfg);

        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.ClearProviders();
            b.AddScryFile(resolved);
        });
        services.AddSingleton<ScryCommands>();
        return services.BuildServiceProvider();
    }
}
