using Backend.Veteriner.Application.Organization.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Organization.Commands.UpdateBillingProfile;

public sealed record UpdateOrganizationBillingProfileCommand(
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
    string? InvoiceDoorNo)
    : IRequest<Result<OrganizationBillingProfileDto>>;
