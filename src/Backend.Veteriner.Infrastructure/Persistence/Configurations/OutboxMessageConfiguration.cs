using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> e)
    {
        e.ToTable("OutboxMessages");
        e.HasKey(x => x.Id);

        e.Property(x => x.Type).IsRequired().HasMaxLength(64);
        e.Property(x => x.Payload).IsRequired(); // uzun JSON olabilir
        e.Property(x => x.CreatedAtUtc).IsRequired();

        e.HasIndex(x => x.NextAttemptAtUtc);
        e.HasIndex(x => new { x.ProcessedAtUtc, x.Type });
        e.HasIndex(x => x.DeadLetterAtUtc);

        // OutboxProcessor: pending satırlar (işlenmemiş, dead-letter değil) + CreatedAtUtc sıralı batch
        e.HasIndex(x => new { x.NextAttemptAtUtc, x.CreatedAtUtc })
            .HasDatabaseName("IX_OutboxMessages_Pending_NextAttemptAtUtc_CreatedAtUtc")
            .HasFilter("[ProcessedAtUtc] IS NULL AND [DeadLetterAtUtc] IS NULL");

        e.HasIndex(x => new { x.AppointmentId, x.AppointmentSequence })
            .HasDatabaseName("IX_OutboxMessages_AppointmentId_AppointmentSequence")
            .IsUnique()
            .HasFilter("[AppointmentId] IS NOT NULL AND [AppointmentSequence] IS NOT NULL");

        e.Property(x => x.ClaimedBy).HasMaxLength(128);

        // Appointment projection atomik claim sorgusu (pending + metadata + sıralama)
        e.HasIndex(x => new { x.CreatedAtUtc, x.AppointmentSequence, x.Id })
            .HasDatabaseName("IX_OutboxMessages_AppointmentProjection_PendingClaim")
            .HasFilter(
                "[ProcessedAtUtc] IS NULL AND [DeadLetterAtUtc] IS NULL AND [AppointmentId] IS NOT NULL AND [AppointmentSequence] IS NOT NULL");
    }
}
