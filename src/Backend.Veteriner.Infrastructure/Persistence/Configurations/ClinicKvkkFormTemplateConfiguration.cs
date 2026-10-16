using Backend.Veteriner.Domain.Clinics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ClinicKvkkFormTemplateConfiguration : IEntityTypeConfiguration<ClinicKvkkFormTemplate>
{
    public void Configure(EntityTypeBuilder<ClinicKvkkFormTemplate> b)
    {
        b.ToTable("ClinicKvkkFormTemplates");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.Kind).IsRequired();
        b.Property(x => x.Text)
            .IsRequired()
            .HasMaxLength(ClinicKvkkFormTemplate.MaxTextLength);
        b.Property(x => x.TextVersion).IsRequired();
        b.Property(x => x.TextHash)
            .IsRequired()
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(ClinicKvkkFormTemplate.TextHashLength);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);
        b.Property(x => x.UpdatedByUserId).IsRequired();
        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.Kind }).IsUnique();

        b.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
