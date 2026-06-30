using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Tenants;

/// <summary>
/// Kiracı firma kimliği ve fatura adresi bilgileri (e-belge entegrasyonu öncesi profil).
/// </summary>
public sealed class TenantBillingProfile : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public string? CompanyName { get; private set; }
    public string? LegalCompanyName { get; private set; }
    public string? TaxOffice { get; private set; }
    public string? TaxNumber { get; private set; }
    public string? CompanyPhone { get; private set; }
    public string? InvoiceProvince { get; private set; }
    public string? InvoiceDistrict { get; private set; }
    public string? InvoiceNeighborhood { get; private set; }
    public string? InvoiceStreet { get; private set; }
    public string? InvoiceBuildingName { get; private set; }
    public string? InvoiceBuildingNo { get; private set; }
    public string? InvoiceDoorNo { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private TenantBillingProfile() { }

    public static TenantBillingProfile CreateEmpty(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));

        return new TenantBillingProfile
        {
            TenantId = tenantId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = null
        };
    }

    public void Update(
        string? companyName,
        string? legalCompanyName,
        string? taxOffice,
        string? taxNumber,
        string? companyPhone,
        string? invoiceProvince,
        string? invoiceDistrict,
        string? invoiceNeighborhood,
        string? invoiceStreet,
        string? invoiceBuildingName,
        string? invoiceBuildingNo,
        string? invoiceDoorNo)
    {
        CompanyName = NormalizeOptional(companyName);
        LegalCompanyName = NormalizeOptional(legalCompanyName);
        TaxOffice = NormalizeOptional(taxOffice);
        TaxNumber = NormalizeOptional(taxNumber);
        CompanyPhone = NormalizeOptional(companyPhone);
        InvoiceProvince = NormalizeOptional(invoiceProvince);
        InvoiceDistrict = NormalizeOptional(invoiceDistrict);
        InvoiceNeighborhood = NormalizeOptional(invoiceNeighborhood);
        InvoiceStreet = NormalizeOptional(invoiceStreet);
        InvoiceBuildingName = NormalizeOptional(invoiceBuildingName);
        InvoiceBuildingNo = NormalizeOptional(invoiceBuildingNo);
        InvoiceDoorNo = NormalizeOptional(invoiceDoorNo);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
