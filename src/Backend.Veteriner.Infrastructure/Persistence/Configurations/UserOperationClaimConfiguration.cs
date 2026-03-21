using Backend.Veteriner.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class UserOperationClaimConfiguration : IEntityTypeConfiguration<UserOperationClaim>
{
    public void Configure(EntityTypeBuilder<UserOperationClaim> b)
    {
        b.ToTable("UserOperationClaims");

        b.HasKey(x => x.Id);
        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.OperationClaimId).IsRequired();

        b.HasIndex(x => new { x.UserId, x.OperationClaimId }).IsUnique();

        // Opsiyonel navigation�lar
        b.HasOne(x => x.User)
         .WithMany()
         .HasForeignKey(x => x.UserId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.OperationClaim)
         .WithMany()
         .HasForeignKey(x => x.OperationClaimId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
