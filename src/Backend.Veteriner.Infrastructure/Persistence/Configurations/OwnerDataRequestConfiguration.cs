using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.OwnerPortal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class OwnerDataRequestConfiguration : IEntityTypeConfiguration<OwnerDataRequest>
{
    public void Configure(EntityTypeBuilder<OwnerDataRequest> b)
    {
        b.ToTable("OwnerDataRequests");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.ClientId).IsRequired();
        b.Property(x => x.OwnerAccountId).IsRequired();
        b.Property(x => x.LinkId).IsRequired();
        b.Property(x => x.Type).IsRequired();
        b.Property(x => x.Message).IsRequired().HasMaxLength(OwnerDataRequest.MessageMaxLength);
        b.Property(x => x.Status).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.DueAtUtc).IsRequired();
        b.Property(x => x.HandledByUserId);
        b.Property(x => x.HandledAtUtc);
        b.Property(x => x.ResponseNote).HasMaxLength(OwnerDataRequest.ResponseNoteMaxLength);
        b.Property(x => x.CancelledAtUtc);
        b.Property(x => x.MutationSequence).IsRequired().HasDefaultValue(0L).IsConcurrencyToken();

        b.HasOne<OwnerAccountClientLink>().WithMany().HasForeignKey(x => x.LinkId).OnDelete(DeleteBehavior.Restrict);

        // Gelen kutusu (klinik + durum) ve portal listesi (bağ).
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.LinkId });
    }
}

public sealed class ClinicPortalSettingsConfiguration : IEntityTypeConfiguration<ClinicPortalSettings>
{
    public void Configure(EntityTypeBuilder<ClinicPortalSettings> b)
    {
        b.ToTable("ClinicPortalSettings");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.IsEnabled).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);
        b.Property(x => x.UpdatedByUserId);

        b.HasIndex(x => new { x.TenantId, x.ClinicId }).IsUnique();

        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
    }
}
