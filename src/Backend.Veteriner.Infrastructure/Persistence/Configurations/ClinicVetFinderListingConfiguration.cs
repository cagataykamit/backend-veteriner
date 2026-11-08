using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.VetFinder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ClinicVetFinderListingConfiguration : IEntityTypeConfiguration<ClinicVetFinderListing>
{
    public void Configure(EntityTypeBuilder<ClinicVetFinderListing> b)
    {
        b.ToTable("ClinicVetFinderListings");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.IsListed).IsRequired();
        b.Property(x => x.PublicKey)
            .IsRequired()
            .HasMaxLength(ClinicVetFinderListing.PublicKeyLength)
            .IsUnicode(false);
        b.Property(x => x.ServiceKeys)
            .IsRequired()
            .HasMaxLength(400)
            .IsUnicode(false);
        b.Ignore(x => x.Services);
        b.Property(x => x.Latitude).HasPrecision(9, 6);
        b.Property(x => x.Longitude).HasPrecision(9, 6);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => new { x.TenantId, x.ClinicId }).IsUnique();

        // Herkese açık uç anahtarı kiracılar arası çözer (TIER-015).
        b.HasIndex(x => x.PublicKey).IsUnique();

        // Liste yalnız listelenen satırları okur.
        b.HasIndex(x => x.IsListed);

        b.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
