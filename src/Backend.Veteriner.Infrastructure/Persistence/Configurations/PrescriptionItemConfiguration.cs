using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class PrescriptionItemConfiguration : IEntityTypeConfiguration<PrescriptionItem>
{
    public void Configure(EntityTypeBuilder<PrescriptionItem> b)
    {
        b.ToTable("PrescriptionItems");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.PrescriptionId).IsRequired();
        b.Property(x => x.SortOrder).IsRequired();
        b.Property(x => x.Name).IsRequired().HasMaxLength(PrescriptionItem.NameMaxLength);
        b.Property(x => x.ProductId);
        b.Property(x => x.Instructions).HasMaxLength(PrescriptionItem.InstructionsMaxLength);
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.Dose).HasMaxLength(PrescriptionItem.DoseMaxLength);
        b.Property(x => x.Unit).HasMaxLength(PrescriptionItem.UnitMaxLength);
        b.Property(x => x.LotNumber).HasMaxLength(PrescriptionItem.LotNumberMaxLength);

        b.HasIndex(x => x.PrescriptionId);
        b.HasIndex(x => x.ProductId);

        b.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
