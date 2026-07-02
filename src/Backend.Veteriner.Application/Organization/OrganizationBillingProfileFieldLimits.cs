namespace Backend.Veteriner.Application.Organization;

internal static class OrganizationBillingProfileFieldLimits
{
    public const int MinTextLength = 2;

    public const int CompanyName = 200;
    public const int LegalCompanyName = 200;
    public const int TaxOffice = 100;
    public const int TaxNumber = 11;
    public const int CompanyPhone = 50;
    public const int InvoiceProvince = 80;
    public const int InvoiceDistrict = 80;
    public const int InvoiceNeighborhood = 120;
    public const int InvoiceStreet = 160;
    public const int InvoiceBuildingName = 120;
    public const int InvoiceBuildingNo = 20;
    public const int InvoiceDoorNo = 20;
}
