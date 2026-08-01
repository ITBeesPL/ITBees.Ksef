using ITBees.Ksef.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ITBees.Ksef.Fas.Setup;

public static class KsefFasServiceCollectionExtensions
{
    /// <summary>
    /// Registers the full KSeF invoicing pipeline for the FAS payment stack: the ITBees.Ksef client
    /// (options bound from the "Ksef" configuration section), the payment-session outbox service and the
    /// background worker. Remember to call <see cref="KsefFasDbModelBuilder.RegisterDbModels"/> in your
    /// DbContext.OnModelCreating and to add a migration for the KsefInvoiceRecord table.
    /// The worker stays inactive until Ksef:KsefToken is configured.
    /// </summary>
    /// <typeparam name="TContext">Host application DbContext (must contain PaymentSession and KsefInvoiceRecord).</typeparam>
    public static IServiceCollection AddKsefFasInvoicing<TContext>(this IServiceCollection services,
        IConfiguration configuration, string sectionName = "Ksef") where TContext : DbContext
    {
        services.AddITBeesKsef(configuration, sectionName);
        services.AddScoped<IKsefPaymentInvoiceService, KsefPaymentInvoiceService<TContext>>();
        services.AddHostedService<KsefInvoiceWorker>();
        return services;
    }
}
