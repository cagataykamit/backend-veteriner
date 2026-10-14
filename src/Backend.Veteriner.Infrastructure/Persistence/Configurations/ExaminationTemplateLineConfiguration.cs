using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ExaminationTemplateLineConfiguration : IEntityTypeConfiguration<ExaminationTemplateLine>
{
    public void Configure(EntityTypeBuilder<ExaminationTemplateLine> b)
    {
        b.ToTable("ExaminationTemplateLines", t =>
            t.HasCheckConstraint("CK_ExaminationTemplateLines_Quantity_Positive", "[Quantity] > 0"));

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.ExaminationTemplateId).IsRequired();
        b.Property(x => x.SortOrder).IsRequired();
        b.Property(x => x.ProductId).IsRequired();
        b.Property(x => x.Quantity).HasPrecision(18, Administration.QuantityDecimals).IsRequired();

        b.HasIndex(x => x.ExaminationTemplateId);
        b.HasIndex(x => x.ProductId);

        b.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
