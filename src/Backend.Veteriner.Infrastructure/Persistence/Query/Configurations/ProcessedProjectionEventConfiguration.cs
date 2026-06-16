using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Query.Configurations;

public sealed class ProcessedProjectionEventConfiguration : IEntityTypeConfiguration<ProcessedProjectionEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedProjectionEvent> b)
    {
        b.ToTable("ProcessedProjectionEvents");
        b.HasKey(x => new { x.EventId, x.ConsumerName });

        b.Property(x => x.ConsumerName)
            .IsRequired()
            .HasMaxLength(QueryReadModelConstraints.ProjectionConsumerName);

        b.Property(x => x.ProcessedAtUtc).IsRequired();
    }
}
