using Backend.Veteriner.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class OperationClaimPermissionConfiguration : IEntityTypeConfiguration<OperationClaimPermission>
{
    public void Configure(EntityTypeBuilder<OperationClaimPermission> b)
    {
        b.ToTable("OperationClaimPermissions");

        b.HasKey(x => x.Id);

        b.Property(x => x.OperationClaimId).IsRequired();
        b.Property(x => x.PermissionId).IsRequired();

        b.HasIndex(x => new { x.OperationClaimId, x.PermissionId }).IsUnique();

        b.HasOne(x => x.Permission)
         .WithMany()
         .HasForeignKey(x => x.PermissionId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<OperationClaim>()
         .WithMany()
         .HasForeignKey(x => x.OperationClaimId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
