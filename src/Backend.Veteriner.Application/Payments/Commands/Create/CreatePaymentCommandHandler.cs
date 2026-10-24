using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Payments.Access;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Payments.Commands.Create;

public sealed class CreatePaymentCommandHandler : IRequestHandler<CreatePaymentCommand, Result<PaymentCreateResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Payment> _paymentsRead;
    private readonly IRepository<Payment> _paymentsWrite;
    private readonly IRepository<Charge> _chargesWrite;
    private readonly IRepository<PaymentAllocation> _allocationsWrite;
    private readonly TimeProvider _timeProvider;
    private readonly IPaymentIntegrationEventOutbox _eventOutbox;

    public CreatePaymentCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Appointment> appointments,
        IReadRepository<Examination> examinations,
        IReadRepository<Payment> paymentsRead,
        IRepository<Payment> paymentsWrite,
        IRepository<Charge> chargesWrite,
        IRepository<PaymentAllocation> allocationsWrite,
        TimeProvider timeProvider,
        IPaymentIntegrationEventOutbox eventOutbox)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _clinics = clinics;
        _clients = clients;
        _pets = pets;
        _appointments = appointments;
        _examinations = examinations;
        _paymentsRead = paymentsRead;
        _paymentsWrite = paymentsWrite;
        _chargesWrite = chargesWrite;
        _allocationsWrite = allocationsWrite;
        _timeProvider = timeProvider;
        _eventOutbox = eventOutbox;
    }

    public async Task<Result<PaymentCreateResultDto>> Handle(CreatePaymentCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PaymentCreateResultDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<PaymentCreateResultDto>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
        {
            return Result<PaymentCreateResultDto>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için ödeme kaydı oluşturulamaz.");
        }

        if (request.Amount <= 0)
        {
            return Result<PaymentCreateResultDto>.Failure(
                "Payments.InvalidAmount",
                "Tutar sıfırdan büyük olmalıdır.");
        }

        var paidUtc = PaymentPaidAtWindow.ToUtc(request.PaidAtUtc);
        var window = PaymentPaidAtWindow.Validate(paidUtc);
        if (!window.IsSuccess)
            return Result<PaymentCreateResultDto>.Failure(window.Error);

        if (_clinicContext.ClinicId.HasValue && request.ClinicId != _clinicContext.ClinicId.Value)
        {
            return Result<PaymentCreateResultDto>.Failure(
                "Payments.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        var effectiveClinicId = _clinicContext.ClinicId ?? request.ClinicId;

        var clinicAccess = await PaymentClinicWriteScope.EnsureWriteAccessAsync(
            _clinicScopeResolver, tenantId, effectiveClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<PaymentCreateResultDto>.Failure(clinicAccess.Error);

        // Tekrar istek: aynı RequestId aynı içerikle ilk ödemeyi döndürür; farklı içerikle çakışmadır.
        var fingerprint = PaymentRequestFingerprint.Compute(request, effectiveClinicId, paidUtc);
        if (request.RequestId != Guid.Empty)
        {
            var existing = await _paymentsRead.FirstOrDefaultAsync(
                new PaymentByRequestIdSpec(tenantId, request.RequestId), ct);
            if (existing is not null)
                return Replay(existing, fingerprint);
        }

        var clinic = await _clinics.FirstOrDefaultAsync(
            new ClinicByIdSpec(tenantId, effectiveClinicId), ct);
        if (clinic is null)
            return Result<PaymentCreateResultDto>.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var client = await _clients.FirstOrDefaultAsync(
            new ClientByIdSpec(tenantId, request.ClientId), ct);
        if (client is null)
            return Result<PaymentCreateResultDto>.Failure("Clients.NotFound", "Müşteri bulunamadı veya kiracıya ait değil.");

        string? petName = null;
        if (request.PetId is { } pid)
        {
            var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, pid), ct);
            if (pet is null)
                return Result<PaymentCreateResultDto>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

            if (pet.ClientId != request.ClientId)
            {
                return Result<PaymentCreateResultDto>.Failure(
                    "Payments.PetClientMismatch",
                    "Seçilen hayvan bu müşteriye ait değil.");
            }

            petName = pet.Name;
        }

        if (request.AppointmentId is { } aid)
        {
            var appt = await _appointments.FirstOrDefaultAsync(
                new AppointmentByIdSpec(tenantId, aid), ct);
            if (appt is null)
            {
                return Result<PaymentCreateResultDto>.Failure(
                    "Appointments.NotFound",
                    "Randevu bulunamadı veya kiracıya ait değil.");
            }

            if (appt.ClinicId != effectiveClinicId)
            {
                return Result<PaymentCreateResultDto>.Failure(
                    "Payments.AppointmentClinicMismatch",
                    "Randevu ile klinik bilgisi uyuşmuyor.");
            }

            var apptPet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, appt.PetId), ct);
            if (apptPet is null || apptPet.ClientId != request.ClientId)
            {
                return Result<PaymentCreateResultDto>.Failure(
                    "Payments.AppointmentClientMismatch",
                    "Randevudaki hayvan bu müşteri ile eşleşmiyor.");
            }

            if (request.PetId.HasValue && appt.PetId != request.PetId.Value)
            {
                return Result<PaymentCreateResultDto>.Failure(
                    "Payments.AppointmentPetMismatch",
                    "Randevu ile seçilen hayvan uyuşmuyor.");
            }
        }

        if (request.ExaminationId is { } eid)
        {
            var exam = await _examinations.FirstOrDefaultAsync(
                new ExaminationByIdSpec(tenantId, eid), ct);
            if (exam is null)
            {
                return Result<PaymentCreateResultDto>.Failure(
                    "Examinations.NotFound",
                    "Muayene bulunamadı veya kiracıya ait değil.");
            }

            if (exam.ClinicId != effectiveClinicId)
            {
                return Result<PaymentCreateResultDto>.Failure(
                    "Payments.ExaminationClinicMismatch",
                    "Muayene ile klinik bilgisi uyuşmuyor.");
            }

            var examPet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, exam.PetId), ct);
            if (examPet is null || examPet.ClientId != request.ClientId)
            {
                return Result<PaymentCreateResultDto>.Failure(
                    "Payments.ExaminationClientMismatch",
                    "Muayenedeki hayvan bu müşteri ile eşleşmiyor.");
            }

            if (request.PetId.HasValue && exam.PetId != request.PetId.Value)
            {
                return Result<PaymentCreateResultDto>.Failure(
                    "Payments.ExaminationPetMismatch",
                    "Muayene ile seçilen hayvan uyuşmuyor.");
            }
        }

        if (request.RequestId == Guid.Empty)
            return Fail("Payments.RequestIdRequired", "requestId zorunludur (çift istek koruması).");

        if (request.Allocations is not { Count: > 0 } allocationLines)
            return Fail("Payments.AllocationsRequired", "Ödeme en az bir ücrete tahsis edilmelidir.");

        if (!Payment.IsSupportedCurrency(request.Currency))
            return Fail("Payments.Validation", "Yeni ödemeler yalnızca TRY para biriminde kaydedilebilir.");

        if (_clientContext.UserId is not { } userId)
            return Fail("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

        // İzlenen ücretler: tahsis sayacı concurrency token ile güncellenir (iki kullanıcı çakışması 409).
        var chargeIds = allocationLines.Select(l => l.ChargeId).Distinct().ToArray();
        var charges = await _chargesWrite.ListAsync(new ChargesByIdsForUpdateSpec(tenantId, chargeIds), ct);
        if (charges.Count != chargeIds.Length)
            return Fail("Charges.NotFound", "Ücret kaydı bulunamadı veya kiracıya ait değil.");

        var chargeById = charges.ToDictionary(c => c.Id);
        var lines = allocationLines.Select(l => (chargeById[l.ChargeId], l.Amount)).ToList();

        // Hayvan/muayene istekte yoksa tahsis edilen ücretlerin ortak bağlamından türetilir (ADR-012).
        var petId = request.PetId ?? PaymentChargeContext.CommonPetId(charges);
        if (petName is null && petId is { } derivedPetId)
        {
            var derivedPet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, derivedPetId), ct);
            petName = derivedPet?.Name;
        }

        var payment = new Payment(
            tenantId,
            effectiveClinicId,
            request.ClientId,
            petId,
            request.AppointmentId,
            request.ExaminationId ?? PaymentChargeContext.CommonExaminationId(charges),
            request.Amount,
            request.Currency,
            request.Method,
            paidUtc,
            request.Notes,
            request.RequestId,
            fingerprint);

        // 1) Önce yalnızca ödeme yazılır: RequestId benzersiz indeksi çift isteği burada eler ve ücretlere dokunulmamış olur.
        try
        {
            await _paymentsWrite.AddAsync(payment, ct);
        }
        catch (DbUpdateException)
        {
            // Eşzamanlı çift istek: kazanan ödeme döner.
            var winner = await _paymentsRead.FirstOrDefaultAsync(
                new PaymentByRequestIdSpec(tenantId, request.RequestId), ct);
            if (winner is null)
                throw;

            return Replay(winner, fingerprint);
        }

        // 2) Tahsis kuralları domain'de; hata dönerse işlem geri alınır (ITransactionalRollbackOnFailureRequest) ve ödeme kalmaz.
        var allocated = payment.AllocateFully(lines, userId, _timeProvider.GetUtcNow().UtcDateTime);
        if (!allocated.IsSuccess)
            return Result<PaymentCreateResultDto>.Failure(allocated.Error);

        try
        {
            // Outbox emission aynı transaction sınırında kalıcı olur (buffer interceptor ile drain edilir).
            await _eventOutbox.EnqueueAsync(
                PaymentIntegrationEventTypes.Created,
                new PaymentCreatedIntegrationEvent(
                    Guid.NewGuid(),
                    _timeProvider.GetUtcNow().UtcDateTime,
                    PaymentProjectionSnapshotFactory.Create(payment, client.FullName, clinic.Name, petName)),
                ct);

            // Tahsis satırları + ödeme/ücret sayaçları + outbox tek SaveChanges'te; sayaç token'ları iki kullanıcı çakışmasını yakalar.
            await _allocationsWrite.AddRangeAsync(allocated.Value!, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail(
                "Payments.ConcurrencyConflict",
                "Ücret eşzamanlı olarak güncellendi; açık tutar yeniden okunup işlem tekrarlanmalı.");
        }

        return Result<PaymentCreateResultDto>.Success(new PaymentCreateResultDto(payment.Id, true));
    }

    private static Result<PaymentCreateResultDto> Replay(Payment existing, string fingerprint)
    {
        if (existing.IsVoided)
        {
            return Fail(
                "Payments.RequestIdVoided",
                "Bu requestId ile kaydedilen ödeme iptal edilmiş; yeni bir requestId kullanılmalı.");
        }

        return string.Equals(existing.RequestHash, fingerprint, StringComparison.Ordinal)
            ? Result<PaymentCreateResultDto>.Success(new PaymentCreateResultDto(existing.Id, false))
            : Fail(
                "Payments.DuplicateRequestMismatch",
                "Bu requestId daha önce farklı bir ödeme isteği için kullanılmış.");
    }

    private static Result<PaymentCreateResultDto> Fail(string code, string message)
        => Result<PaymentCreateResultDto>.Failure(code, message);
}
