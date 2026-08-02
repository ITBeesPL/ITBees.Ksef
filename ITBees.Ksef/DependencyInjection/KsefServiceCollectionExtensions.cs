using ITBees.Ksef.Auth;
using ITBees.Ksef.Configuration;
using ITBees.Ksef.Http;
using ITBees.Ksef.Invoicing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ITBees.Ksef.DependencyInjection;

public static class KsefServiceCollectionExtensions
{
    /// <summary>Registers KSeF services, binding <see cref="KsefOptions"/> from the given configuration section (default "Ksef").</summary>
    public static IServiceCollection AddITBeesKsef(this IServiceCollection services, IConfiguration configuration,
        string sectionName = "Ksef")
    {
        var section = configuration.GetSection(sectionName);

        // Common aliases like "prod" would make options binding throw at first access and could take
        // the whole host down - map them to valid KsefEnvironment names before binding.
        var normalizedEnvironment = NormalizeEnvironmentAlias(section[nameof(KsefOptions.Environment)]);
        if (normalizedEnvironment != null)
        {
            var patchedConfiguration = new ConfigurationBuilder()
                .AddConfiguration(configuration)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{sectionName}:{nameof(KsefOptions.Environment)}"] = normalizedEnvironment
                })
                .Build();
            section = patchedConfiguration.GetSection(sectionName);
        }

        services.Configure<KsefOptions>(section);
        return services.AddITBeesKsefCore();
    }

    /// <summary>
    /// Maps common environment aliases to valid <see cref="KsefEnvironment"/> names.
    /// Returns null when the value is already valid (or empty) - unknown values are left as-is,
    /// so the binding error surfaces in logs instead of silently picking a wrong environment.
    /// </summary>
    private static string? NormalizeEnvironmentAlias(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        var value = rawValue.Trim();
        if (Enum.TryParse<Configuration.KsefEnvironment>(value, true, out _))
            return null;

        return value.ToLowerInvariant() switch
        {
            "prod" or "produkcja" or "produkcyjne" => nameof(Configuration.KsefEnvironment.Production),
            "sandbox" or "testy" or "testowe" => nameof(Configuration.KsefEnvironment.Test),
            "preprod" or "pre-prod" or "staging" => nameof(Configuration.KsefEnvironment.Demo),
            _ => null
        };
    }

    public static IServiceCollection AddITBeesKsef(this IServiceCollection services,
        Action<KsefOptions> configureOptions)
    {
        services.Configure(configureOptions);
        return services.AddITBeesKsefCore();
    }

    private static IServiceCollection AddITBeesKsefCore(this IServiceCollection services)
    {
        services.AddHttpClient<IKsefApiClient, KsefApiClient>((serviceProvider, http) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<KsefOptions>>().Value;
            http.BaseAddress = new Uri(options.GetBaseUrl() + "/");
            http.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
        });

        services.AddSingleton<IKsefAuthenticationService, KsefAuthenticationService>();
        services.AddSingleton<IFa3XmlGenerator, Fa3XmlGenerator>();
        services.AddTransient<IKsefInvoiceService, KsefInvoiceService>();
        return services;
    }
}
