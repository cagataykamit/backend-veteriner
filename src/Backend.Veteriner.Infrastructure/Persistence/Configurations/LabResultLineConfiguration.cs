using Backend.Veteriner.Domain.LabResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class LabResultLineConfiguration : IEntityTypeConfiguration<LabResultLine>
{
    public void Configure(EntityTypeBuilder<LabResultLine> b)
    {
        b.ToTable("LabResultLines");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.LabResultId).IsRequired();
        b.Property(x => x.SortOrder).IsRequired();
        b.Property(x => x.ParameterName).IsRequired().HasMaxLength(LabResultLine.ParameterNameMaxLength);
        b.Property(x => x.ParameterKey).IsRequired().HasMaxLength(LabResultLine.ParameterNameMaxLength);
        b.Property(x => x.Value).HasPrecision(18, LabResultLine.ValueScale);
        b.Property(x => x.Unit).HasMaxLength(LabResultLine.UnitMaxLength);
        b.Property(x => x.ReferenceLow).HasPrecision(18, LabResultLine.ValueScale);
        b.Property(x => x.ReferenceHigh).HasPrecision(18, LabResultLine.ValueScale);

        b.Ignore(x => x.Flag);

        b.HasIndex(x => new { x.LabResultId, x.ParameterKey });
    }
}
