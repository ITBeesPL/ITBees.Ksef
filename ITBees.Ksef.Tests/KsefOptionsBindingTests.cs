using ITBees.Ksef.Configuration;
using ITBees.Ksef.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ITBees.Ksef.Tests;

public class KsefOptionsBindingTests
{
    [Theory]
    [InlineData("Test", KsefEnvironment.Test)]
    [InlineData("demo", KsefEnvironment.Demo)]
    [InlineData("Production", KsefEnvironment.Production)]
    [InlineData("prod", KsefEnvironment.Production)]
    [InlineData("produkcja", KsefEnvironment.Production)]
    [InlineData("sandbox", KsefEnvironment.Test)]
    [InlineData("preprod", KsefEnvironment.Demo)]
    public void AddITBeesKsef_BindsEnvironmentIncludingAliases(string configuredValue, KsefEnvironment expected)
    {
        var options = BindOptions(configuredValue);

        Assert.Equal(expected, options.Value.Environment);
    }

    [Fact]
    public void AddITBeesKsef_UnknownEnvironmentValue_ThrowsOnAccessNotOnRegistration()
    {
        // Rejestracja nie może rzucać - błąd ma wyjść dopiero przy odczycie opcji,
        // gdzie KsefInvoiceWorker łapie go i wyłącza fakturowanie zamiast kłaść hosta.
        var options = BindOptions("something-invalid");

        Assert.ThrowsAny<Exception>(() => options.Value);
    }

    private static IOptions<KsefOptions> BindOptions(string environmentValue)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ksef:Environment"] = environmentValue,
                ["Ksef:KsefToken"] = "token"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddITBeesKsef(configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptions<KsefOptions>>();
    }
}
