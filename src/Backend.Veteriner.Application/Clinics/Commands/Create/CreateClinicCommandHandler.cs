using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Commands.Create;

public sealed class CreateClinicCommandHandler : IRequestHandler<CreateClinicCommand, Result<Guid>>
{
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinicsRead;
    private readonly IRepository<Clinic> _clinicsWrite;

    public CreateClinicCommandHandler(
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinicsRead,
        IRepository<Clinic> clinicsWrite)
    {
        _tenants = tenants;
        _clinicsRead = clinicsRead;
        _clinicsWrite = clinicsWrite;
    }

    public async Task<Result<Guid>> Handle(CreateClinicCommand request, CancellationToken ct)
    {
        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(request.TenantId), ct);
        if (tenant is null)
            return Result<Guid>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Result<Guid>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için klinik oluşturulamaz.");

        var nameKey = request.Name.Trim().ToLowerInvariant();
        var duplicate = await _clinicsRead.FirstOrDefaultAsync(
            new ClinicByTenantAndNameCaseInsensitiveSpec(request.TenantId, nameKey), ct);
        if (duplicate is not null)
            return Result<Guid>.Failure(
                "Clinics.DuplicateName",
                "Bu kiracı altında aynı isimde bir klinik zaten var.");

        var clinic = new Clinic(request.TenantId, request.Name, request.City);
        await _clinicsWrite.AddAsync(clinic, ct);
        await _clinicsWrite.SaveChangesAsync(ct);
        return Result<Guid>.Success(clinic.Id);
    }
}
