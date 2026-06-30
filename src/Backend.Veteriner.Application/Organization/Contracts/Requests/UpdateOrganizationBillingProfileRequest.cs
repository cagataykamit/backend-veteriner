namespace Backend.Veteriner.Application.Organization.Contracts.Requests;

public sealed record UpdateOrganizationBillingProfileRequest(
    string? CompanyName,
    string? LegalCompanyName,
    string? TaxOffice,
    string? TaxNumber,
    string? CompanyPhone,
    string? InvoiceProvince,
    string? InvoiceDistrict,
    string? InvoiceNeighborhood,
    string? InvoiceStreet,
    string? InvoiceBuildingName,
    string? InvoiceBuildingNo,
    string? InvoiceDoorNo);
