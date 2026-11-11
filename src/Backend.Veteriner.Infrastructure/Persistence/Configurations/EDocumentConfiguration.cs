using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class EDocumentConfiguration : IEntityTypeConfiguration<EDocument>
{
    /// <summary>Aynı istemci isteği (RequestId) kiracıda en fazla bir belge üretir.</summary>
    public const string RequestIdIndexName = "UX_EDocuments_TenantId_RequestId";

    public void Configure(EntityTypeBuilder<EDocument> b)
    {
        b.ToTable("EDocuments", t =>
        {
            t.HasCheckConstraint("CK_EDocuments_TotalAmount_Positive", "[TotalAmount] > 0");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.ClientId).IsRequired();
        b.Property(x => x.SaleId);
        b.Property(x => x.VisitId);
        b.Property(x => x.Type).IsRequired().HasConversion<int>();
        b.Property(x => x.Status).IsRequired().HasConversion<int>();
        b.Property(x => x.Environment).IsRequired().HasConversion<int>();
        b.Property(x => x.Origin).IsRequired().HasConversion<int>().HasDefaultValue(EDocumentOrigin.Manual);
        b.Property(x => x.ProviderCode).IsRequired().IsUnicode(false).HasMaxLength(EDocument.MaxProviderCodeLength);
        b.Property(x => x.ExternalReference).IsRequired();
        b.Property(x => x.RequestId).IsRequired();
        b.Property(x => x.RequestHash).IsRequired().IsUnicode(false).HasMaxLength(EDocument.MaxRequestHashLength);
        b.Property(x => x.Note).HasMaxLength(EDocumentRules.MaxNoteLength);
        b.Property(x => x.TotalAmount).HasPrecision(18, 2).IsRequired();
        b.Property(x => x.WithholdingRate).HasPrecision(5, TaxRatePercent.Decimals);

        b.Property(x => x.ProviderInvoiceId).IsUnicode(false).HasMaxLength(EDocument.MaxProviderIdLength);
        b.Property(x => x.ProviderDocumentId).IsUnicode(false).HasMaxLength(EDocument.MaxProviderIdLength);
        b.Property(x => x.ProviderJobId).IsUnicode(false).HasMaxLength(EDocument.MaxProviderIdLength);
        b.Property(x => x.DocumentNumber).HasMaxLength(EDocument.MaxDocumentNumberLength);
        b.Property(x => x.DocumentUuid).IsUnicode(false).HasMaxLength(EDocument.MaxProviderIdLength);
        b.Property(x => x.ProviderTotal).HasPrecision(18, 2);
        b.Property(x => x.IssuedAtUtc);

        b.Property(x => x.LastErrorKind).HasConversion<int?>();
        b.Property(x => x.LastErrorMessage).HasMaxLength(EDocument.MaxErrorMessageLength);
        b.Property(x => x.LastProviderErrorCode).HasMaxLength(EDocument.MaxProviderErrorCodeLength);
        b.Property(x => x.AttemptCount).IsRequired();
        b.Property(x => x.CheckCount).IsRequired();
        b.Property(x => x.NextCheckAtUtc);

        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);
        b.Property(x => x.CancelReason).HasMaxLength(EDocumentRules.MaxReasonLength);
        b.Property(x => x.DiscardReason).HasMaxLength(EDocumentRules.MaxReasonLength);

        b.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

        b.OwnsOne(x => x.Buyer, buyer =>
        {
            buyer.Property(p => p.Kind).HasColumnName("BuyerKind").IsRequired().HasConversion<int>();
            buyer.Property(p => p.Name).HasColumnName("BuyerName").IsRequired().HasMaxLength(EDocumentBuyer.MaxNameLength);
            buyer.Property(p => p.IdentityNumber).HasColumnName("BuyerIdentityNumber").IsRequired().IsUnicode(false)
                .HasMaxLength(TurkishTaxIdentity.TaxpayerIdentityMaxLength);
            buyer.Property(p => p.IsUnknownBuyer).HasColumnName("BuyerIsUnknown").IsRequired();
            buyer.Property(p => p.TaxOffice).HasColumnName("BuyerTaxOffice").HasMaxLength(Client.MaxTaxOfficeLength);
            buyer.Property(p => p.Province).HasColumnName("BuyerProvince").HasMaxLength(Client.MaxBillingProvinceLength);
            buyer.Property(p => p.District).HasColumnName("BuyerDistrict").HasMaxLength(Client.MaxBillingDistrictLength);
            buyer.Property(p => p.AddressLine).HasColumnName("BuyerAddressLine").HasMaxLength(Client.MaxBillingAddressLineLength);
            buyer.Property(p => p.Email).HasColumnName("BuyerEmail").HasMaxLength(320);
        });
        b.Navigation(x => x.Buyer).IsRequired();

        b.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(l => l.EDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.TenantId, x.RequestId }).IsUnique().HasDatabaseName(RequestIdIndexName);
        b.HasIndex(x => x.ExternalReference).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ClientId });
        b.HasIndex(x => new { x.Status, x.NextCheckAtUtc });
    }
}

public sealed class ClinicEDocumentSettingsConfiguration : IEntityTypeConfiguration<ClinicEDocumentSettings>
{
    public void Configure(EntityTypeBuilder<ClinicEDocumentSettings> b)
    {
        b.ToTable("ClinicEDocumentSettings");

        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.AutoIssueMode).IsRequired().HasConversion<int>();
        b.Property(x => x.EndOfDayTime).IsRequired();
        b.Property(x => x.IssueOnPaymentByDefault).IsRequired();
        b.Property(x => x.AutoIssueEnabledAtUtc);
        b.Property(x => x.LastEndOfDayRunDate);
        b.Property(x => x.UpdatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

        b.HasIndex(x => new { x.TenantId, x.ClinicId }).IsUnique();

        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EDocumentLineConfiguration : IEntityTypeConfiguration<EDocumentLine>
{
    public void Configure(EntityTypeBuilder<EDocumentLine> b)
    {
        b.ToTable("EDocumentLines", t =>
        {
            t.HasCheckConstraint("CK_EDocumentLines_GrossAmount_Positive", "[GrossAmount] > 0");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.EDocumentId).IsRequired();
        b.Property(x => x.ChargeId).IsRequired();
        b.Property(x => x.Position).IsRequired();
        b.Property(x => x.Description).IsRequired().HasMaxLength(Charge.MaxDescriptionLength);
        b.Property(x => x.Quantity).HasPrecision(18, Charge.QuantityDecimals).IsRequired();
        b.Property(x => x.Unit).HasMaxLength(50);
        b.Property(x => x.GrossAmount).HasPrecision(18, 2).IsRequired();
        b.Property(x => x.VatRate).HasPrecision(5, TaxRatePercent.Decimals).IsRequired();
        b.Property(x => x.LineKind).IsRequired().HasConversion<int>();

        b.HasIndex(x => x.ChargeId);
    }
}

public sealed class EDocumentConnectionConfiguration : IEntityTypeConfiguration<EDocumentConnection>
{
    public void Configure(EntityTypeBuilder<EDocumentConnection> b)
    {
        b.ToTable("EDocumentConnections");

        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.ProviderCode).IsRequired().IsUnicode(false).HasMaxLength(EDocument.MaxProviderCodeLength);
        b.Property(x => x.Environment).IsRequired().HasConversion<int>();
        b.Property(x => x.Status).IsRequired().HasConversion<int>();
        b.Property(x => x.AccountId).IsUnicode(false).HasMaxLength(EDocumentConnection.MaxAccountIdLength);
        b.Property(x => x.AccountName).HasMaxLength(EDocumentConnection.MaxAccountNameLength);
        // Data Protection çıktısı (base64url); belirteç boyutu sağlayıcıya göre değişir.
        b.Property(x => x.ProtectedCredentials).IsUnicode(false);
        b.Property(x => x.LastVerificationMessage).HasMaxLength(EDocumentConnection.MaxMessageLength);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

        b.HasIndex(x => new { x.TenantId, x.ClinicId }).IsUnique();

        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
    }
}
