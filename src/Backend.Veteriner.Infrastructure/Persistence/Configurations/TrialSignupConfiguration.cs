using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class SignupEmailVerificationConfiguration : IEntityTypeConfiguration<SignupEmailVerification>
{
    public void Configure(EntityTypeBuilder<SignupEmailVerification> b)
    {
        b.ToTable("SignupEmailVerifications");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Email).IsRequired().HasMaxLength(SignupEmailVerification.EmailMaxLength);
        b.Property(x => x.CodeHash).IsRequired().HasMaxLength(SignupEmailVerification.HashMaxLength).IsUnicode(false);
        b.Property(x => x.RequestIpHash).HasMaxLength(SignupEmailVerification.HashMaxLength).IsUnicode(false);
        b.Property(x => x.SignupTokenHash).HasMaxLength(SignupEmailVerification.HashMaxLength).IsUnicode(false);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.ExpiresAtUtc).IsRequired();

        // Bekleme, adres sınırı ve son kod; IP ve genel günlük sınır.
        b.HasIndex(x => new { x.Email, x.CreatedAtUtc });
        b.HasIndex(x => new { x.RequestIpHash, x.CreatedAtUtc });
        b.HasIndex(x => x.CreatedAtUtc);
    }
}

public sealed class TrialSignupIdentityConfiguration : IEntityTypeConfiguration<TrialSignupIdentity>
{
    public void Configure(EntityTypeBuilder<TrialSignupIdentity> b)
    {
        b.ToTable("TrialSignupIdentities");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.EmailKey).IsRequired().HasMaxLength(TrialSignupIdentity.EmailKeyMaxLength);
        b.Property(x => x.TaxIdentityNumber).HasMaxLength(TurkishTaxIdentity.TaxpayerIdentityMaxLength).IsUnicode(false);
        b.Property(x => x.CreatedAtUtc).IsRequired();

        // Tek deneme kuralı veritabanında da: yarışta ikinci kayıt düşer. Kiracı silinse de satır kalır (yabancı anahtar yok).
        b.HasIndex(x => x.EmailKey).IsUnique();
        b.HasIndex(x => x.TaxIdentityNumber).IsUnique().HasFilter("[TaxIdentityNumber] IS NOT NULL");
        b.HasIndex(x => x.TenantId);
    }
}
