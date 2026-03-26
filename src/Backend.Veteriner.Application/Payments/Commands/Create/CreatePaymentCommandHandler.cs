using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.Payments.Commands.Create;

public sealed class CreatePaymentCommandHandler : IRequestHandler<CreatePaymentCommand, Result<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IRepository<Payment> _paymentsWrite;

    public CreatePaymentCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Appointment> appointments,
        IReadRepository<Examination> examinations,
        IRepository<Payment> paymentsWrite)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _tenants = tenants;
        _clinics = clinics;
        _clients = clients;
        _pets = pets;
        _appointments = appointments;
        _examinations = examinations;
        _paymentsWrite = paymentsWrite;
    }

    public async Task<Result<Guid>> Handle(CreatePaymentCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<Guid>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<Guid>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
        {
            return Result<Guid>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için ödeme kaydı oluşturulamaz.");
        }

        if (request.Amount <= 0)
        {
            return Result<Guid>.Failure(
                "Payments.InvalidAmount",
                "Tutar sıfırdan büyük olmalıdır.");
        }

        var paidUtc = PaymentPaidAtWindow.ToUtc(request.PaidAtUtc);
        var window = PaymentPaidAtWindow.Validate(paidUtc);
        if (!window.IsSuccess)
            return Result<Guid>.Failure(window.Error);

        if (_clinicContext.ClinicId.HasValue && request.ClinicId != _clinicContext.ClinicId.Value)
        {
            return Result<Guid>.Failure(
                "Payments.ClinicContextMismatch",
                "Istek clinicId degeri aktif clinic baglami ile uyusmuyor.");
        }

        var effectiveClinicId = _clinicContext.ClinicId ?? request.ClinicId;

        var clinic = await _clinics.FirstOrDefaultAsync(
            new ClinicByIdSpec(tenantId, effectiveClinicId), ct);
        if (clinic is null)
            return Result<Guid>.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var client = await _clients.FirstOrDefaultAsync(
            new ClientByIdSpec(tenantId, request.ClientId), ct);
        if (client is null)
            return Result<Guid>.Failure("Clients.NotFound", "Müşteri bulunamadı veya kiracıya ait değil.");

        if (request.PetId is { } pid)
        {
            var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, pid), ct);
            if (pet is null)
                return Result<Guid>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

            if (pet.ClientId != request.ClientId)
            {
                return Result<Guid>.Failure(
                    "Payments.PetClientMismatch",
                    "Seçilen hayvan bu müşteriye ait değil.");
            }
        }

        if (request.AppointmentId is { } aid)
        {
            var appt = await _appointments.FirstOrDefaultAsync(
                new AppointmentByIdSpec(tenantId, aid), ct);
            if (appt is null)
            {
                return Result<Guid>.Failure(
                    "Appointments.NotFound",
                    "Randevu bulunamadı veya kiracıya ait değil.");
            }

            if (appt.ClinicId != effectiveClinicId)
            {
                return Result<Guid>.Failure(
                    "Payments.AppointmentClinicMismatch",
                    "Randevu ile klinik bilgisi uyuşmuyor.");
            }

            var apptPet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, appt.PetId), ct);
            if (apptPet is null || apptPet.ClientId != request.ClientId)
            {
                return Result<Guid>.Failure(
                    "Payments.AppointmentClientMismatch",
                    "Randevudaki hayvan bu müşteri ile eşleşmiyor.");
            }

            if (request.PetId.HasValue && appt.PetId != request.PetId.Value)
            {
                return Result<Guid>.Failure(
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
                return Result<Guid>.Failure(
                    "Examinations.NotFound",
                    "Muayene bulunamadı veya kiracıya ait değil.");
            }

            if (exam.ClinicId != effectiveClinicId)
            {
                return Result<Guid>.Failure(
                    "Payments.ExaminationClinicMismatch",
                    "Muayene ile klinik bilgisi uyuşmuyor.");
            }

            var examPet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, exam.PetId), ct);
            if (examPet is null || examPet.ClientId != request.ClientId)
            {
                return Result<Guid>.Failure(
                    "Payments.ExaminationClientMismatch",
                    "Muayenedeki hayvan bu müşteri ile eşleşmiyor.");
            }

            if (request.PetId.HasValue && exam.PetId != request.PetId.Value)
            {
                return Result<Guid>.Failure(
                    "Payments.ExaminationPetMismatch",
                    "Muayene ile seçilen hayvan uyuşmuyor.");
            }
        }

        var payment = new Payment(
            tenantId,
            effectiveClinicId,
            request.ClientId,
            request.PetId,
            request.AppointmentId,
            request.ExaminationId,
            request.Amount,
            request.Currency,
            request.Method,
            paidUtc,
            request.Notes);

        await _paymentsWrite.AddAsync(payment, ct);
        await _paymentsWrite.SaveChangesAsync(ct);
        return Result<Guid>.Success(payment.Id);
    }
}
