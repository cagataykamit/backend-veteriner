using Backend.Veteriner.Application.Administrations.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hbs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Application.StockBatches.Specs;
using Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using Mediator;

namespace Backend.Veteriner.Application.Vaccinations.Queries.GetById;

public sealed class GetVaccinationByIdQueryHandler
    : IRequestHandler<GetVaccinationByIdQuery, Result<VaccinationDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IUserOperationClaimRepository _userOperationClaims;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IReadRepository<Administration> _administrations;
    private readonly IReadRepository<Product> _products;
    private readonly IReadRepository<StockBatch> _batches;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IClinicVeterinarianReader _names;

    public GetVaccinationByIdQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IUserOperationClaimRepository userOperationClaims,
        IUserClinicRepository userClinics,
        IReadRepository<Vaccination> vaccinations,
        IReadRepository<Administration> administrations,
        IReadRepository<Product> products,
        IReadRepository<StockBatch> batches,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IClinicVeterinarianReader names)
    {
        _names = names;
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _userOperationClaims = userOperationClaims;
        _userClinics = userClinics;
        _vaccinations = vaccinations;
        _administrations = administrations;
        _products = products;
        _batches = batches;
        _pets = pets;
        _clients = clients;
    }

    public async ValueTask<Result<VaccinationDetailDto>> Handle(GetVaccinationByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<VaccinationDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<VaccinationDetailDto>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var v = await _vaccinations.FirstOrDefaultAsync(
            new VaccinationByIdSpec(tenantId, request.Id), ct);
        if (v is null)
            return Result<VaccinationDetailDto>.Failure("Vaccinations.NotFound", "Aşı kaydı bulunamadı.");
        if (_clinicContext.ClinicId is { } clinicId && v.ClinicId != clinicId)
            return Result<VaccinationDetailDto>.Failure("Vaccinations.NotFound", "Aşı kaydı bulunamadı.");

        var operationClaimNames = await _userOperationClaims.GetOperationClaimNamesByUserIdAsync(userId, ct);
        if (!TenantWideClaimNames.IsTenantWide(operationClaimNames))
        {
            if (!await _userClinics.ExistsAsync(userId, v.ClinicId, ct))
            {
                return Result<VaccinationDetailDto>.Failure(
                    "Vaccinations.NotFound",
                    "Aşı kaydı bulunamadı.");
            }
        }

        var pets = await _pets.ListAsync(new PetsByTenantIdsSpec(tenantId, [v.PetId]), ct);
        var pet = pets.FirstOrDefault();
        var petName = pet?.Name ?? string.Empty;
        var clientId = pet?.ClientId ?? Guid.Empty;
        string clientName = string.Empty;
        if (pet is not null)
        {
            var clients = await _clients.ListAsync(new ClientsByTenantIdsSpec(tenantId, [pet.ClientId]), ct);
            clientName = clients.FirstOrDefault()?.FullName ?? string.Empty;
        }

        var administration = await _administrations.FirstOrDefaultAsync(
            new AdministrationByVaccinationIdSpec(tenantId, v.Id), ct);
        VaccinationAdministrationDto? administrationDto = null;
        if (administration is not null)
        {
            var product = await _products.FirstOrDefaultAsync(
                new ProductByIdSpec(tenantId, administration.ProductId), ct);
            var batch = administration.BatchId is { } batchId
                ? await _batches.FirstOrDefaultAsync(new StockBatchByIdSpec(tenantId, batchId), ct)
                : null;
            administrationDto = new VaccinationAdministrationDto(
                administration.Id,
                administration.ProductId,
                product?.Name ?? string.Empty,
                administration.Quantity,
                product?.Unit ?? string.Empty,
                administration.BatchId,
                batch?.LotNumber,
                batch?.ExpiryDate);
        }

        var dto = new VaccinationDetailDto(
            v.Id,
            v.TenantId,
            v.PetId,
            petName,
            clientName,
            clientId,
            v.ClinicId,
            v.ExaminationId,
            v.VaccineDefinitionId,
            v.VaccineName,
            v.AppliedAtUtc,
            v.DueAtUtc,
            v.Status,
            v.Notes,
            v.CreatedAtUtc,
            v.UpdatedAtUtc,
            administrationDto,
            await HbsEntryDtoFactory.ForVaccinationAsync(v, _names, ct));
        return Result<VaccinationDetailDto>.Success(dto);
    }
}
