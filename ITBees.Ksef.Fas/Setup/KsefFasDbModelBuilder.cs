using Microsoft.EntityFrameworkCore;

namespace ITBees.Ksef.Fas.Setup;

public static class KsefFasDbModelBuilder
{
    /// <summary>Call from your DbContext.OnModelCreating to register the KSeF invoice outbox table.</summary>
    public static void RegisterDbModels(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KsefInvoiceRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            // One invoice per payment session (idempotency), gapless-per-month numbering guard.
            entity.HasIndex(x => x.PaymentSessionGuid).IsUnique();
            entity.HasIndex(x => new { x.Year, x.Month, x.SequenceNumber }).IsUnique();
            entity.Property(x => x.InvoiceNumber).HasMaxLength(64);
            entity.Property(x => x.KsefNumber).HasMaxLength(64);
            entity.Property(x => x.KsefSessionReferenceNumber).HasMaxLength(64);
            entity.Property(x => x.LastError).HasMaxLength(2048);
        });
    }
}
