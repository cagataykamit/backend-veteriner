using Backend.Veteriner.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class OperationClaimConfiguration : IEntityTypeConfiguration<OperationClaim>
{
    public void Configure(EntityTypeBuilder<OperationClaim> b)
    {
        b.ToTable("OperationClaims");
        b.HasKey(x => x.Id);

        b.Property(x => x.Name)
         .IsRequired()
         .HasMaxLength(128);

        b.HasIndex(x => x.Name).IsUnique();
    }
}
