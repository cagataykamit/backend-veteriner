using Backend.Veteriner.Domain.OwnerPortal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>Hasta sahibi portal hesabı (ADR-031). E-posta benzersiz; silinen hesapta boştur (ADR-041), indeks yalnız dolu e-postaları kapsar.</summary>
public sealed class OwnerAccountConfiguration : IEntityTypeConfiguration<OwnerAccount>
{
    public void Configure(EntityTypeBuilder<OwnerAccount> b)
    {
        b.ToTable("OwnerAccounts");
        b.HasKey(x => x.Id);

        b.Property(x => x.Email).HasMaxLength(OwnerAccount.MaxEmailLength);
        b.Property(x => x.PasswordHash).HasMaxLength(200);
        b.Property(x => x.DisplayName).IsRequired().HasMaxLength(OwnerAccount.MaxDisplayNameLength);
        b.Property(x => x.Status).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.FailedSignInCount).IsRequired();

        b.HasIndex(x => x.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
    }
}
