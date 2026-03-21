using Backend.Veteriner.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> e)
    {
        e.ToTable("RefreshTokens");
        e.HasKey(x => x.Id);

        // Hash alanları: varchar + BIN2 (tam byte eşleşmesi)
        e.Property(x => x.TokenHash)
            .IsRequired()
            .HasMaxLength(86)
            .IsUnicode(false)
            .UseCollation("Latin1_General_100_BIN2");

        e.Property(x => x.ReplacedByTokenHash)
            .HasMaxLength(86)
            .IsUnicode(false)
            .UseCollation("Latin1_General_100_BIN2");

        e.Property(x => x.ExpiresAtUtc).IsRequired();
        e.Property(x => x.CreatedAtUtc).IsRequired();

        e.Property(x => x.LastUsedAtUtc)
            .IsRequired(false);

        // RevokeReason: iki seçenek
        // 1) Kurumsal/operasyonel metin => Unicode gerekmez diyorsanız varchar yapın:
        e.Property(x => x.RevokeReason)
            .HasMaxLength(128)
            .IsUnicode(false)
            .IsRequired(false);

        // Teknik alanlar: varchar (Unicode uyarıları ve gereksiz alan maliyeti kalkar)
        e.Property(x => x.IpAddress)
            .HasMaxLength(64)
            .IsUnicode(false);

        e.Property(x => x.UserAgent)
            .HasMaxLength(512)
            .IsUnicode(false);

        // Indexler
        e.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasDatabaseName("IX_RefreshTokens_TokenHash");

        e.HasIndex(x => new { x.UserId, x.RevokedAtUtc })
            .HasDatabaseName("IX_RefreshTokens_User_RevokedAt");

        e.HasIndex(x => x.ExpiresAtUtc)
            .HasDatabaseName("IX_RefreshTokens_ExpiresAt");

        e.HasIndex(x => x.ReplacedByTokenHash)
            .HasDatabaseName("IX_RefreshTokens_ReplacedByTokenHash");

        // İlişki
        e.HasOne(x => x.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
