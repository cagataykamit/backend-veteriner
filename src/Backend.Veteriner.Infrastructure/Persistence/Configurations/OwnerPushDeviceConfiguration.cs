using Backend.Veteriner.Domain.OwnerPortal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>Hasta sahibi push cihazları (MOBILE-003). Jeton sistem genelinde benzersiz; hesap silinince satırlar da silinir.</summary>
public sealed class OwnerPushDeviceConfiguration : IEntityTypeConfiguration<OwnerPushDevice>
{
    public void Configure(EntityTypeBuilder<OwnerPushDevice> b)
    {
        b.ToTable("OwnerPushDevices");
        b.HasKey(x => x.Id);

        b.Property(x => x.OwnerAccountId).IsRequired();
        b.Property(x => x.Platform).IsRequired();
        b.Property(x => x.Token).IsRequired().HasMaxLength(OwnerPushDevice.MaxTokenLength);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.LastSeenAtUtc).IsRequired();

        b.HasIndex(x => x.Token).IsUnique();
        b.HasIndex(x => x.OwnerAccountId);

        b.HasOne<OwnerAccount>()
            .WithMany()
            .HasForeignKey(x => x.OwnerAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
