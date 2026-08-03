using System.Reflection;
using ITBees.Interfaces.Repository;
using ITBees.Ksef.Credentials;
using ITBees.Ksef.Credentials.Security;
using ITBees.Ksef.Credentials.Setup;
using ITBees.Ksef.Credentials.Controllers;
using ITBees.Ksef.DependencyInjection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ITBees.Ksef.Tests;

public class KsefCredentialsRegistrationTests
{
    /// <summary>
    /// The whole point of the package is that a host wires up four things and gets a working
    /// credential store; if a constructor drifts away from what AddKsefCredentials registers,
    /// the host would only find out when the first request hits the endpoint.
    /// </summary>
    [Fact]
    public void AddKsefCredentials_registers_everything_the_service_needs()
    {
        var services = BuildHost();

        // ValidateOnBuild walks every constructor in the graph, so an unregistered dependency of
        // KsefCredentialService fails here rather than on the host's first request. The service itself
        // is not resolved: constructing it would run the host's storage stubs.
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IKsefCredentialService)
                                                && descriptor.ImplementationType == typeof(KsefCredentialService));
        Assert.IsType<AesSecretProtector>(provider.GetRequiredService<ISecretProtector>());
        Assert.NotNull(provider.GetRequiredService<IKsefClientFactory>());
    }

    [Fact]
    public void Credential_changes_are_not_audited_unless_the_host_asks_for_it()
    {
        using var provider = BuildHost().BuildServiceProvider();

        Assert.IsType<NullKsefCredentialAuditSink>(
            provider.CreateScope().ServiceProvider.GetRequiredService<IKsefCredentialAuditSink>());
    }

    [Fact]
    public void Host_audit_sink_wins_over_the_no_op_default()
    {
        var services = BuildHost();
        services.AddScoped<IKsefCredentialAuditSink, RecordingAuditSink>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<RecordingAuditSink>(
            provider.CreateScope().ServiceProvider.GetRequiredService<IKsefCredentialAuditSink>());
    }

    /// <summary>
    /// The endpoints live in this assembly, so the host's own controller scan cannot see them —
    /// AddKsefCredentialControllers is what makes them routable, and nothing else in a host's build
    /// would fail if that call went missing.
    /// </summary>
    [Fact]
    public void Credential_endpoints_are_discoverable_as_an_application_part()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddKsefCredentialControllers();

        var manager = services.Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ApplicationPartManager>()
            .Single();

        var controllers = new ControllerFeature();
        manager.PopulateFeature(controllers);

        Assert.Contains(typeof(KsefCredentialController).GetTypeInfo(), controllers.Controllers);
        Assert.Contains(typeof(KsefConnectionTestController).GetTypeInfo(), controllers.Controllers);
    }

    private static ServiceCollection BuildHost()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Factory registrations satisfy the dependency without being constructed, which is exactly
        // what the host provides in real life — storage and "who am I" are its business.
        services.AddScoped<IReadOnlyRepository<KsefCredential>>(_ => throw new NotSupportedException());
        services.AddScoped<IWriteOnlyRepository<KsefCredential>>(_ => throw new NotSupportedException());
        services.AddScoped<IKsefCompanyContext>(_ => throw new NotSupportedException());

        services.AddKsefCredentials(options => options.EncryptionKey = Convert.ToBase64String(new byte[32]));
        return services;
    }

    private sealed class RecordingAuditSink : IKsefCredentialAuditSink
    {
        public void Created(Guid companyGuid, KsefCredentialAuditView credential)
        {
        }

        public void Updated(Guid companyGuid, KsefCredentialAuditView before, KsefCredentialAuditView after)
        {
        }

        public void Deleted(Guid companyGuid, KsefCredentialAuditView credential)
        {
        }

        public void ConnectionTested(Guid companyGuid, KsefCredentialAuditView credential, bool success, string? error)
        {
        }
    }
}
