using Backend.Veteriner.Domain.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> b)
    {
        b.ToTable("Clients");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId)
            .IsRequired();

        b.Property(x => x.FullName)
            .IsRequired()
            .HasMaxLength(300);

        b.Property(x => x.Email)
            .HasMaxLength(320);

        b.Property(x => x.Phone)
            .HasMaxLength(50);

        b.Property(x => x.PhoneNormalized)
            .HasMaxLength(50);

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.FullName });

        b.HasIndex(x => new { x.TenantId, x.Email, x.PhoneNormalized })
            .IsUnique()
            .HasFilter("[Email] IS NOT NULL AND [PhoneNormalized] IS NOT NULL");
    }
}
