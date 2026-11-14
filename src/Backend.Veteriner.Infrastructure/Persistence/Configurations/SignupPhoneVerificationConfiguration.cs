using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class SignupPhoneVerificationConfiguration : IEntityTypeConfiguration<SignupPhoneVerification>
{
    public void Configure(EntityTypeBuilder<SignupPhoneVerification> b)
    {
        b.ToTable("SignupPhoneVerifications");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Phone).IsRequired().HasMaxLength(User.PhoneNumberLength).IsUnicode(false);
        b.Property(x => x.CodeHash).IsRequired().HasMaxLength(SignupPhoneVerification.HashMaxLength).IsUnicode(false);
        b.Property(x => x.RequestIpHash).HasMaxLength(SignupPhoneVerification.HashMaxLength).IsUnicode(false);
        b.Property(x => x.SignupTokenHash).HasMaxLength(SignupPhoneVerification.HashMaxLength).IsUnicode(false);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.ExpiresAtUtc).IsRequired();

        // Bir numarayla yalnız bir deneme hesabı: kullanılmış satır numara başına tek (yarışta ikinci kayıt da düşer).
        b.HasIndex(x => x.Phone)
            .IsUnique()
            .HasFilter("[UsedAtUtc] IS NOT NULL")
            .HasDatabaseName("UX_SignupPhoneVerifications_Phone_Used");

        // Bekleme, numara sınırı ve son kod; IP ve genel günlük sınır.
        b.HasIndex(x => new { x.Phone, x.CreatedAtUtc });
        b.HasIndex(x => new { x.RequestIpHash, x.CreatedAtUtc });
        b.HasIndex(x => x.CreatedAtUtc);
    }
}
