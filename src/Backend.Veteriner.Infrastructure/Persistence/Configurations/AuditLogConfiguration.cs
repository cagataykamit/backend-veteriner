using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");

        b.HasKey(x => x.Id);

        b.Property(x => x.ActorUserId);

        b.Property(x => x.Action)
            .IsRequired()
            .HasMaxLength(200);

        b.Property(x => x.TargetType)
            .HasMaxLength(200);

        b.Property(x => x.TargetId)
            .HasMaxLength(200);

        b.Property(x => x.Success)
            .IsRequired();

        b.Property(x => x.FailureReason)
            .HasMaxLength(2000);

        b.Property(x => x.Route)
            .HasMaxLength(500);

        b.Property(x => x.HttpMethod)
            .HasMaxLength(20);

        b.Property(x => x.IpAddress)
            .HasMaxLength(100);

        b.Property(x => x.UserAgent)
            .HasMaxLength(1000);

        b.Property(x => x.CorrelationId)
            .HasMaxLength(100);

        b.Property(x => x.RequestName)
            .IsRequired()
            .HasMaxLength(300);

        b.Property(x => x.RequestPayload)
            .HasColumnType("nvarchar(max)");

        b.Property(x => x.OccurredAtUtc)
            .IsRequired();

        // INDEXLER

        b.HasIndex(x => x.OccurredAtUtc);

        b.HasIndex(x => x.ActorUserId);

        b.HasIndex(x => x.Action);

        b.HasIndex(x => x.CorrelationId);
    }
}