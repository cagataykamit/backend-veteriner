using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ClinicTaxpayerSettingsConfiguration : IEntityTypeConfiguration<ClinicTaxpayerSettings>
{
    public void Configure(EntityTypeBuilder<ClinicTaxpayerSettings> b)
    {
        b.ToTable("ClinicTaxpayerSettings");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.TaxpayerType).HasConversion<int?>();
        b.Property(x => x.TaxIdentityNumber).IsUnicode(false).HasMaxLength(TurkishTaxIdentity.TaxpayerIdentityMaxLength);
        b.Property(x => x.TaxOffice).HasMaxLength(ClinicTaxpayerSettings.MaxTaxOfficeLength);
        b.Property(x => x.TaxpayerName).HasMaxLength(ClinicTaxpayerSettings.MaxTaxpayerNameLength);
        b.Property(x => x.Province).HasMaxLength(ClinicTaxpayerSettings.MaxProvinceLength);
        b.Property(x => x.District).HasMaxLength(ClinicTaxpayerSettings.MaxDistrictLength);
        b.Property(x => x.AddressLine).HasMaxLength(ClinicTaxpayerSettings.MaxAddressLineLength);
        b.Property(x => x.DefaultVatRate).IsRequired().HasPrecision(5, TaxRatePercent.Decimals);
        b.Property(x => x.WithholdingRate).HasPrecision(5, TaxRatePercent.Decimals);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);
        b.Property(x => x.UpdatedByUserId).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.ClinicId }).IsUnique();

        b.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
