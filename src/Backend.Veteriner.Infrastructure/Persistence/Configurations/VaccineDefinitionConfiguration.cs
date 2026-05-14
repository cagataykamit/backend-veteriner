using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class VaccineDefinitionConfiguration : IEntityTypeConfiguration<VaccineDefinition>
{
    public void Configure(EntityTypeBuilder<VaccineDefinition> b)
    {
        b.ToTable("VaccineDefinitions");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId);
        b.Property(x => x.SpeciesId);

        b.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        b.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(80);

        b.Property(x => x.Description)
            .HasMaxLength(1000);

        b.Property(x => x.DefaultNextDueDays);
        b.Property(x => x.IsCore).IsRequired();
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => new { x.TenantId, x.IsActive });
        b.HasIndex(x => new { x.SpeciesId, x.IsActive });
        b.HasIndex(x => x.Name);
        b.HasIndex(x => x.Code)
            .IsUnique()
            .HasFilter("[TenantId] IS NULL");

        b.HasIndex(x => new { x.TenantId, x.Code })
            .IsUnique()
            .HasFilter("[TenantId] IS NOT NULL");

        b.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        b.HasOne<Species>()
            .WithMany()
            .HasForeignKey(x => x.SpeciesId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
