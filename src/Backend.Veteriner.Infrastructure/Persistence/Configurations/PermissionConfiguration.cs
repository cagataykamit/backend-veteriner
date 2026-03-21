using Backend.Veteriner.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("Permissions");

        b.HasKey(x => x.Id);

        b.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(128);

        b.Property(x => x.Description)
            .HasMaxLength(512);

        b.Property(x => x.Group)
            .HasMaxLength(128);

        b.HasIndex(x => x.Code)
            .IsUnique();
    }
}