using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Queries.GetExampleClinicStatus;

/// <summary>Kiracıda örnek klinik verisi var mı ve temizlik neyi silecek (EXAMPLE_CLINIC_API_CONTRACT.md §10.2).</summary>
public sealed record GetExampleClinicStatusQuery(Guid TenantId) : IRequest<Result<ExampleClinicStatusDto>>;
