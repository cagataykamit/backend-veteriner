using Backend.Veteriner.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class VerificationTokenConfiguration : IEntityTypeConfiguration<VerificationToken>
{
    public void Configure(EntityTypeBuilder<VerificationToken> e)
    {
        e.ToTable("VerificationTokens");
        e.HasKey(x => x.Id);

        e.Property(x => x.TokenHash).IsRequired().HasMaxLength(128);
        e.HasIndex(x => x.TokenHash).IsUnique();

        e.Property(x => x.Purpose).IsRequired();
        e.Property(x => x.ExpiresAtUtc).IsRequired();

        e.HasOne(x => x.User)
         .WithMany() // ayr� koleksiyon tutmuyoruz; istersen Users taraf�na ICollection ekleyebilirsin
         .HasForeignKey(x => x.UserId);
    }
}
