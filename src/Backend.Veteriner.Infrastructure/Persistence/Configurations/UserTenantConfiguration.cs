using Backend.Veteriner.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class UserTenantConfiguration : IEntityTypeConfiguration<UserTenant>
{
    public void Configure(EntityTypeBuilder<UserTenant> b)
    {
        b.ToTable("UserTenants");
        b.HasKey(x => x.Id);

        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();

        // Kullanıcı başına tek kiracı üyeliği (nihai SaaS modeli)
        b.HasIndex(x => x.UserId).IsUnique();

        b.HasIndex(x => x.TenantId);

        b.HasIndex(x => new { x.UserId, x.TenantId });

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Tenant)
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
