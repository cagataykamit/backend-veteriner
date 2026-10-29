using System.Text.Json;
using Backend.Veteriner.Domain.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ReportSavedFilterConfiguration : IEntityTypeConfiguration<ReportSavedFilter>
{
    public void Configure(EntityTypeBuilder<ReportSavedFilter> b)
    {
        b.ToTable("ReportSavedFilters");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.ReportKey).IsRequired().HasMaxLength(ReportKey.MaxLength);
        b.Property(x => x.Name).IsRequired().HasMaxLength(ReportSavedFilter.MaxNameLength);
        b.Property(x => x.NormalizedName).IsRequired().HasMaxLength(ReportSavedFilter.MaxNameLength);
        b.Property(x => x.CreatedAtUtc).IsRequired();

        // Filtre seti JSON nesnesi olarak tek kolonda (backend alanları yorumlamaz; boyut alanda sınırlı).
        b.Property(x => x.Filters)
            .HasColumnName("FiltersJson")
            .IsRequired()
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, string>(),
                new ValueComparer<IReadOnlyDictionary<string, string>>(
                    (a, c) => a!.Count == c!.Count && !a.Except(c).Any(),
                    v => v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key, kv.Value)),
                    v => new Dictionary<string, string>(v)));

        b.HasIndex(x => new { x.TenantId, x.UserId, x.ReportKey, x.NormalizedName }).IsUnique();
    }
}
