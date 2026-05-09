using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.ToTable("StockMovements");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.ProductId).IsRequired();

        b.Property(x => x.MovementType).IsRequired();
        b.Property(x => x.Quantity)
            .HasPrecision(18, 3)
            .IsRequired();

        b.Property(x => x.UnitCost)
            .HasPrecision(18, 2);

        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.ReferenceType).HasMaxLength(100);
        b.Property(x => x.ReferenceId);
        b.Property(x => x.OccurredAtUtc).IsRequired();
        b.Property(x => x.CreatedByUserId);
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.Property(x => x.CreatedAtUtc).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.OccurredAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.ProductId, x.OccurredAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ProductId });

        b.HasOne(x => x.Product)
            .WithMany(p => p.Movements)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
