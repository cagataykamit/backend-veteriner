namespace Backend.Veteriner.Application.Organization.Contracts.Dtos;

public sealed record OrganizationBillingProfileDto(
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
