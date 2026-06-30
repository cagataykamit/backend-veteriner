using Backend.Veteriner.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class TenantBillingProfileConfiguration : IEntityTypeConfiguration<TenantBillingProfile>
{
    public void Configure(EntityTypeBuilder<TenantBillingProfile> b)
    {
        b.ToTable("TenantBillingProfiles");
        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.CompanyName).HasMaxLength(200);
        b.Property(x => x.LegalCompanyName).HasMaxLength(300);
        b.Property(x => x.TaxOffice).HasMaxLength(100);
        b.Property(x => x.TaxNumber).HasMaxLength(11);
        b.Property(x => x.CompanyPhone).HasMaxLength(50);
        b.Property(x => x.InvoiceProvince).HasMaxLength(100);
        b.Property(x => x.InvoiceDistrict).HasMaxLength(100);
        b.Property(x => x.InvoiceNeighborhood).HasMaxLength(150);
        b.Property(x => x.InvoiceStreet).HasMaxLength(200);
        b.Property(x => x.InvoiceBuildingName).HasMaxLength(100);
        b.Property(x => x.InvoiceBuildingNo).HasMaxLength(20);
        b.Property(x => x.InvoiceDoorNo).HasMaxLength(20);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => x.TenantId).IsUnique();
    }
}
