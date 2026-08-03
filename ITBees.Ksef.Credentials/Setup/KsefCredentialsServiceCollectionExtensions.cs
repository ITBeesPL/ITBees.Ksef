using ITBees.Ksef.Credentials.Controllers;
using ITBees.Ksef.Credentials.Security;
using ITBees.Ksef.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ITBees.Ksef.Credentials.Setup;

public static class KsefCredentialsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the per-company KSeF credential store: <see cref="IKsefClientFactory"/>, the AES secret
    /// protector and <see cref="IKsefCredentialService"/>.
    /// </summary>
    /// <remarks>
    /// The host still has to provide:
    /// <list type="bullet">
    /// <item><description><see cref="IKsefCompanyContext"/> — which company the current request acts on;</description></item>
    /// <item><description><c>IReadOnlyRepository&lt;KsefCredential&gt;</c> and <c>IWriteOnlyRepository&lt;KsefCredential&gt;</c>;</description></item>
    /// <item><description><see cref="KsefCredentialsDbModelBuilder.RegisterDbModels"/> in its DbContext, plus a migration;</description></item>
    /// <item><description><see cref="AddKsefCredentialControllers"/> on its <see cref="IMvcBuilder"/> to expose the endpoints;</description></item>
    /// <item><description>optionally <see cref="IKsefCredentialAuditSink"/> — without one, changes are not audited.</description></item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddKsefCredentials(this IServiceCollection services,
        Action<KsefCredentialsOptions> configureOptions)
    {
        services.Configure(configureOptions);
        return services.AddKsefCredentialsCore();
    }

    /// <summary>
    /// Same as <see cref="AddKsefCredentials(IServiceCollection,Action{KsefCredentialsOptions})"/> for hosts
    /// that fill <see cref="KsefCredentialsOptions"/> from their own
    /// <see cref="IConfigureOptions{TOptions}"/> — typically because the encryption key needs other
    /// services (configuration, logging) to resolve.
    /// </summary>
    public static IServiceCollection AddKsefCredentials(this IServiceCollection services)
    {
        services.AddOptions<KsefCredentialsOptions>();
        return services.AddKsefCredentialsCore();
    }

    /// <summary>
    /// Same as <see cref="AddKsefCredentials(IServiceCollection,Action{KsefCredentialsOptions})"/>,
    /// binding <see cref="KsefCredentialsOptions"/> from a configuration section (default "KsefCredentials").
    /// </summary>
    public static IServiceCollection AddKsefCredentials(this IServiceCollection services,
        IConfiguration configuration, string sectionName = "KsefCredentials")
    {
        services.Configure<KsefCredentialsOptions>(configuration.GetSection(sectionName));
        return services.AddKsefCredentialsCore();
    }

    /// <summary>
    /// Makes the KSeF credential endpoints (<c>/KsefCredential</c>, <c>/KsefConnectionTest</c>) visible
    /// to MVC — they live in this assembly, so they are not discovered by the host's own scan.
    /// </summary>
    public static IMvcBuilder AddKsefCredentialControllers(this IMvcBuilder builder) =>
        builder.AddApplicationPart(typeof(KsefCredentialController).Assembly);

    private static IServiceCollection AddKsefCredentialsCore(this IServiceCollection services)
    {
        // Every company logs in with its own NIP and its own token/certificate, so we take the factory
        // instead of a globally bound "Ksef" configuration section.
        services.AddITBeesKsefClientFactory();

        services.TryAddSingleton<ISecretProtector, AesSecretProtector>();
        services.TryAddScoped<IKsefCredentialAuditSink, NullKsefCredentialAuditSink>();
        services.AddScoped<IKsefCredentialService, KsefCredentialService>();
        return services;
    }
}
