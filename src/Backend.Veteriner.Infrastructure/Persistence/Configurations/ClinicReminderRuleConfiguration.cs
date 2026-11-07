using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Reminders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ClinicReminderRuleConfiguration : IEntityTypeConfiguration<ClinicReminderRule>
{
    public void Configure(EntityTypeBuilder<ClinicReminderRule> b)
    {
        b.ToTable("ClinicReminderRules");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.RuleType).IsRequired().HasConversion<int>();
        b.Property(x => x.IsEnabled).IsRequired();
        b.Property(x => x.Days).IsRequired();
        b.Property(x => x.EnabledAtUtc);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);
        b.Property(x => x.UpdatedByUserId);

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.RuleType }).IsUnique();

        // İşleyici açık kuralları kiracılar arası tarar.
        b.HasIndex(x => x.IsEnabled);

        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
    }
}
