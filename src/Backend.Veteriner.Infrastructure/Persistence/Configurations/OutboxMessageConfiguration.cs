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
    }
}
