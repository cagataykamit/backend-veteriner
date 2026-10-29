using System.Text.Json;
using Backend.Veteriner.Domain.RecordHistory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class RecordChangeConfiguration : IEntityTypeConfiguration<RecordChange>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<RecordChange> b)
    {
        b.ToTable("RecordChanges");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.RecordType).IsRequired().HasMaxLength(RecordChange.RecordTypeMaxLength);
        b.Property(x => x.RecordId).IsRequired();
        b.Property(x => x.Kind).IsRequired().HasConversion<int>();
        b.Property(x => x.ActorUserId).IsRequired();
        b.Property(x => x.OccurredAtUtc).IsRequired();

        // Alan farkları tek sütunda JSON dizi; yalnız geçmiş ekranında okunur, sorgulanmaz.
        b.Property(x => x.Changes)
            .HasColumnName("ChangesJson")
            .IsRequired()
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<RecordFieldChange>>(v, JsonOptions) ?? new List<RecordFieldChange>(),
                new ValueComparer<IReadOnlyList<RecordFieldChange>>(
                    (a, c) => a!.SequenceEqual(c!),
                    v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x.GetHashCode())),
                    v => v.ToList()));

        b.HasIndex(x => new { x.TenantId, x.RecordType, x.RecordId, x.OccurredAtUtc });
    }
}
