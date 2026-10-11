using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Visits;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Charges.Commands.Create;

public sealed class CreateChargeCommandHandler : IRequestHandler<CreateChargeCommand, Result<ChargeCreateResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Visit> _visits;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Charge> _chargesRead;
    private readonly IRepository<Charge> _chargesWrite;
    private readonly TimeProvider _timeProvider;

    public CreateChargeCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Visit> visits,
        IReadRepository<Examination> examinations,
        IReadRepository<Charge> chargesRead,
        IRepository<Charge> chargesWrite,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _clients = clients;
        _pets = pets;
        _visits = visits;
        _examinations = examinations;
        _chargesRead = chargesRead;
        _chargesWrite = chargesWrite;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ChargeCreateResultDto>> Handle(CreateChargeCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Fail(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
            return Fail("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Fail("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Fail("Tenants.TenantInactive", "Pasif kiracı için ücret kaydı oluşturulamaz.");

        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue
            && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Fail(
                "Charges.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        if ((_clinicContext.ClinicId ?? request.ClinicId) is not { } clinicId)
            return Fail("Charges.Validation", "ClinicId zorunludur (aktif klinik bağlamı yok).");

        var clinicAccess = await _clinicScopeResolver.ResolveAsync(tenantId, clinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<ChargeCreateResultDto>.Failure(clinicAccess.Error);

        if (request.ClientId == Guid.Empty)
            return Fail("Charges.Validation", "ClientId zorunludur.");

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.ClientId), ct);
        if (client is null)
            return Fail("Clients.NotFound", "Müşteri bulunamadı veya kiracıya ait değil.");

        // Tekrar istek: aynı RequestId ilk kaydı döndürür; başka müşteri/klinik için kullanılmışsa reddedilir.
        if (request.RequestId is { } requestId)
        {
            var existing = await _chargesRead.FirstOrDefaultAsync(new ChargeByRequestIdSpec(tenantId, requestId), ct);
            if (existing is not null)
                return await ExistingAsync(existing, clinicId, client, ct);
        }

        var petId = request.PetId;
        Pet? pet = null;
        if (petId is { } requestedPetId)
        {
            pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, requestedPetId), ct);
            if (pet is null)
                return Fail("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

            if (pet.ClientId != request.ClientId)
                return Fail("Charges.PetClientMismatch", "Seçilen hayvan bu müşteriye ait değil.");
        }

        if (request.VisitId is { } visitId)
        {
            var visit = await _visits.FirstOrDefaultAsync(new VisitByIdSpec(tenantId, visitId, asNoTracking: true), ct);
            if (visit is null)
                return Fail("Visits.NotFound", "Geliş kaydı bulunamadı veya kiracıya ait değil.");

            if (visit.IsVoided)
                return Fail("Charges.VisitVoided", "Yanlış geliş olarak işaretli kayda ücret yazılamaz.");

            if (visit.ClinicId != clinicId)
                return Fail("Charges.VisitClinicMismatch", "Geliş kaydı ile klinik bilgisi uyuşmuyor.");

            if (petId.HasValue && visit.PetId != petId.Value)
                return Fail("Charges.VisitPetMismatch", "Geliş kaydı ile seçilen hayvan uyuşmuyor.");

            pet ??= await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, visit.PetId), ct);
            if (pet is null || pet.ClientId != request.ClientId)
                return Fail("Charges.VisitClientMismatch", "Geliş kaydındaki hayvan bu müşteriye ait değil.");

            petId = pet.Id;
        }

        if (request.ExaminationId is { } examinationId)
        {
            var exam = await _examinations.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, examinationId), ct);
            if (exam is null)
                return Fail("Examinations.NotFound", "Muayene bulunamadı veya kiracıya ait değil.");

            if (exam.ClinicId != clinicId)
                return Fail("Charges.ExaminationClinicMismatch", "Muayene ile klinik bilgisi uyuşmuyor.");

            if (petId.HasValue && exam.PetId != petId.Value)
                return Fail("Charges.ExaminationPetMismatch", "Muayene ile seçilen hayvan uyuşmuyor.");

            if (request.VisitId is { } linkedVisitId && exam.VisitId.HasValue && exam.VisitId.Value != linkedVisitId)
                return Fail("Charges.ExaminationVisitMismatch", "Muayene başka bir geliş kaydına bağlı.");

            pet ??= await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, exam.PetId), ct);
            if (pet is null || pet.ClientId != request.ClientId)
                return Fail("Charges.ExaminationClientMismatch", "Muayenedeki hayvan bu müşteriye ait değil.");

            petId = pet.Id;
        }

        var created = Charge.Create(
            tenantId,
            clinicId,
            request.ClientId,
            petId,
            request.VisitId,
            request.ExaminationId,
            request.Description,
            request.Quantity,
            request.UnitPrice,
            userId,
            _timeProvider.GetUtcNow().UtcDateTime,
            request.RequestId);
        if (!created.IsSuccess)
            return Result<ChargeCreateResultDto>.Failure(created.Error);

        var charge = created.Value!;
        try
        {
            // Repository AddAsync kaydı hemen kalıcılaştırır (SaveChanges içerir).
            await _chargesWrite.AddAsync(charge, ct);
        }
        catch (DbUpdateException) when (request.RequestId.HasValue)
        {
            // Eşzamanlı çift istek: filtreli benzersiz indeks kaybedene çarptı; kazanan ücret döner.
            var winner = await _chargesRead.FirstOrDefaultAsync(
                new ChargeByRequestIdSpec(tenantId, request.RequestId.Value), ct);
            if (winner is null)
                throw;

            return await ExistingAsync(winner, clinicId, client, ct);
        }

        return Result<ChargeCreateResultDto>.Success(
            new ChargeCreateResultDto(true, charge.ToDto(client.FullName, pet?.Name)));
    }

    private async Task<Result<ChargeCreateResultDto>> ExistingAsync(
        Charge existing, Guid clinicId, Client client, CancellationToken ct)
    {
        if (existing.ClinicId != clinicId || existing.ClientId != client.Id)
            return Fail("Charges.Validation", "RequestId başka bir ücret isteği için kullanılmış.");

        var petName = existing.PetId is { } petId
            ? (await _pets.FirstOrDefaultAsync(new PetByIdSpec(existing.TenantId, petId), ct))?.Name
            : null;

        return Result<ChargeCreateResultDto>.Success(
            new ChargeCreateResultDto(false, existing.ToDto(client.FullName, petName)));
    }

    private static Result<ChargeCreateResultDto> Fail(string code, string message)
        => Result<ChargeCreateResultDto>.Failure(code, message);
}
