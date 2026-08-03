using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITBees.Ksef.Credentials.Setup;

public static class KsefCredentialsDbModelBuilder
{
    /// <summary>
    /// Call from your DbContext.OnModelCreating to map <see cref="KsefCredential"/>, then add
    /// a migration for the table. Expose it as <c>DbSet&lt;KsefCredential&gt; KsefCredentials</c>
    /// so the table is named <c>KsefCredentials</c>.
    /// </summary>
    /// <param name="modelBuilder">The host DbContext's model builder.</param>
    /// <param name="customize">
    /// Runs after the defaults, for anything only the host can express: the foreign key to its own
    /// company table (<c>HasOne&lt;Company&gt;().WithMany().HasForeignKey(x =&gt; x.CompanyGuid)</c>)
    /// and provider-specific column types. Secret columns are left unbounded here so the mapping works
    /// on any provider — pin them if your database already has narrower columns.
    /// </param>
    public static void RegisterDbModels(ModelBuilder modelBuilder,
        Action<EntityTypeBuilder<KsefCredential>>? customize = null)
    {
        modelBuilder.Entity<KsefCredential>(entity =>
        {
            entity.HasKey(x => x.Guid);
            entity.Property(x => x.Nip).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Kind).HasConversion<int>();
            entity.Property(x => x.Environment).HasConversion<int>();
            entity.Property(x => x.CertificateFileName).HasMaxLength(260);
            entity.Property(x => x.CertificateSubject).HasMaxLength(500);
            entity.Property(x => x.CertificateThumbprint).HasMaxLength(100);
            // One company holds one set of credentials — a token OR a certificate.
            entity.HasIndex(x => x.CompanyGuid).IsUnique();

            customize?.Invoke(entity);
        });
    }
}
