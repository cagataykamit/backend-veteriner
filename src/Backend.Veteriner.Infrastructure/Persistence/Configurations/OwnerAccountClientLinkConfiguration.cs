using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.OwnerPortal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>Hesap ↔ müşteri bağı. Bir müşteri kaydında en fazla bir aktif bağ (filtreli benzersiz indeks).</summary>
public sealed class OwnerAccountClientLinkConfiguration : IEntityTypeConfiguration<OwnerAccountClientLink>
{
    public void Configure(EntityTypeBuilder<OwnerAccountClientLink> b)
    {
        b.ToTable("OwnerAccountClientLinks");
        b.HasKey(x => x.Id);

        b.Property(x => x.OwnerAccountId).IsRequired();
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.ClientId).IsRequired();
        b.Property(x => x.Status).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.CreatedByUserId).IsRequired();

        b.HasIndex(x => x.OwnerAccountId);
        b.HasIndex(x => new { x.TenantId, x.ClientId }, "UX_OwnerAccountClientLinks_ActivePerClient")
            .IsUnique()
            .HasFilter($"[Status] = {(int)OwnerAccountClientLinkStatus.Active}");

        b.HasOne<OwnerAccount>()
            .WithMany()
            .HasForeignKey(x => x.OwnerAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Client>()
            .WithMany()
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
