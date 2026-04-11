using Backend.Veteriner.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ScheduledSubscriptionPlanChangeConfiguration : IEntityTypeConfiguration<ScheduledSubscriptionPlanChange>
{
    public void Configure(EntityTypeBuilder<ScheduledSubscriptionPlanChange> b)
    {
        b.ToTable("ScheduledSubscriptionPlanChanges");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.CurrentPlanCode).IsRequired().HasConversion<int>();
        b.Property(x => x.TargetPlanCode).IsRequired().HasConversion<int>();
        b.Property(x => x.ChangeType).IsRequired().HasConversion<int>();
        b.Property(x => x.Status).IsRequired().HasConversion<int>();
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.RequestedAtUtc).IsRequired();
        b.Property(x => x.EffectiveAtUtc).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.Status }).HasDatabaseName("IX_ScheduledPlanChange_Tenant_Status");
        b.HasIndex(x => new { x.Status, x.EffectiveAtUtc }).HasDatabaseName("IX_ScheduledPlanChange_Status_Effective");

        b.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
