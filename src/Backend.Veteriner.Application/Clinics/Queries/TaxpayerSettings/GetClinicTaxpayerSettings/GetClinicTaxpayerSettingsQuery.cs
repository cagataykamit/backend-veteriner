using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Queries.TaxpayerSettings.GetClinicTaxpayerSettings;

public sealed record GetClinicTaxpayerSettingsQuery(Guid ClinicId) : IRequest<Result<ClinicTaxpayerSettingsDto>>;
