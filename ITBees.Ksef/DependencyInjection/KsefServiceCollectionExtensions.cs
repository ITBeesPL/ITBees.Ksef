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
        services.Configure<KsefOptions>(configuration.GetSection(sectionName));
        return services.AddITBeesKsefCore();
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
