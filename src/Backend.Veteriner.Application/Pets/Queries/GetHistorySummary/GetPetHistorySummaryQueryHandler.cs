using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Treatments;
using MediatR;

namespace Backend.Veteriner.Application.Pets.Queries.GetHistorySummary;

public sealed class GetPetHistorySummaryQueryHandler
    : IRequestHandler<GetPetHistorySummaryQuery, Result<PetHistorySummaryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Treatment> _treatments;
    private readonly IReadRepository<Prescription> _prescriptions;
    private readonly IReadRepository<LabResult> _labResults;
    private readonly IReadRepository<Hospitalization> _hospitalizations;
    private readonly IReadRepository<Payment> _payments;

    public GetPetHistorySummaryQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IReadRepository<Clinic> clinics,
        IReadRepository<Appointment> appointments,
        IReadRepository<Examination> examinations,
        IReadRepository<Treatment> treatments,
        IReadRepository<Prescription> prescriptions,
        IReadRepository<LabResult> labResults,
        IReadRepository<Hospitalization> hospitalizations,
        IReadRepository<Payment> payments)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _pets = pets;
        _clients = clients;
        _clinics = clinics;
        _appointments = appointments;
        _examinations = examinations;
        _treatments = treatments;
        _prescriptions = prescriptions;
        _labResults = labResults;
        _hospitalizations = hospitalizations;
        _payments = payments;
    }

    public async Task<Result<PetHistorySummaryDto>> Handle(GetPetHistorySummaryQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PetHistorySummaryDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Result<PetHistorySummaryDto>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, pet.ClientId), ct);
        var clientName = client?.FullName ?? string.Empty;

        var clinicId = _clinicContext.ClinicId;
        var take = PetHistorySummaryConstants.RecentItemsTake;

        var appts = await _appointments.ListAsync(new AppointmentsForPetRecentSpec(tenantId, clinicId, request.PetId, take), ct);
        var exams = await _examinations.ListAsync(new ExaminationsForPetRecentSpec(tenantId, clinicId, request.PetId, take), ct);
        var treatments = await _treatments.ListAsync(new TreatmentsForPetRecentSpec(tenantId, clinicId, request.PetId, take), ct);
        var rx = await _prescriptions.ListAsync(new PrescriptionsForPetRecentSpec(tenantId, clinicId, request.PetId, take), ct);
        var labs = await _labResults.ListAsync(new LabResultsForPetRecentSpec(tenantId, clinicId, request.PetId, take), ct);
        var hosp = await _hospitalizations.ListAsync(new HospitalizationsForPetRecentSpec(tenantId, clinicId, request.PetId, take), ct);
        var pays = await _payments.ListAsync(new PaymentsForPetRecentSpec(tenantId, clinicId, request.PetId, take), ct);

        var clinicIds = new HashSet<Guid>();
        foreach (var a in appts) clinicIds.Add(a.ClinicId);
        foreach (var e in exams) clinicIds.Add(e.ClinicId);
        foreach (var t in treatments) clinicIds.Add(t.ClinicId);
        foreach (var p in rx) clinicIds.Add(p.ClinicId);
        foreach (var l in labs) clinicIds.Add(l.ClinicId);
        foreach (var h in hosp) clinicIds.Add(h.ClinicId);
        foreach (var pay in pays) clinicIds.Add(pay.ClinicId);

        var clinicIdArr = clinicIds.ToArray();
        var clinicRows = clinicIdArr.Length == 0
            ? []
            : await _clinics.ListAsync(new ClinicsByTenantIdsSpec(tenantId, clinicIdArr), ct);
        var clinicNameById = clinicRows.ToDictionary(x => x.Id, x => x.Name);

        string ClinicName(Guid id) => clinicNameById.GetValueOrDefault(id, string.Empty);

        var recentAppts = appts
            .Select(a => new PetHistoryAppointmentItemDto(
                a.Id,
                a.ScheduledAtUtc,
                a.ClinicId,
                ClinicName(a.ClinicId),
                a.Status,
                a.AppointmentType,
                a.Notes))
            .ToList();

        var recentExams = exams
            .Select(e => new PetHistoryExaminationItemDto(
                e.Id,
                e.ExaminedAtUtc,
                e.ClinicId,
                ClinicName(e.ClinicId),
                e.VisitReason))
            .ToList();

        var recentTreatments = treatments
            .Select(t => new PetHistoryTreatmentItemDto(
                t.Id,
                t.TreatmentDateUtc,
                t.ClinicId,
                ClinicName(t.ClinicId),
                t.Title,
                t.ExaminationId))
            .ToList();

        var recentRx = rx
            .Select(p => new PetHistoryPrescriptionItemDto(
                p.Id,
                p.PrescribedAtUtc,
                p.ClinicId,
                ClinicName(p.ClinicId),
                p.Title,
                p.ExaminationId,
                p.TreatmentId))
            .ToList();

        var recentLabs = labs
            .Select(l => new PetHistoryLabResultItemDto(
                l.Id,
                l.ResultDateUtc,
                l.ClinicId,
                ClinicName(l.ClinicId),
                l.TestName,
                l.ExaminationId))
            .ToList();

        var recentHosp = hosp
            .Select(h => new PetHistoryHospitalizationItemDto(
                h.Id,
                h.AdmittedAtUtc,
                h.ClinicId,
                ClinicName(h.ClinicId),
                h.Reason,
                h.DischargedAtUtc,
                h.DischargedAtUtc is null))
            .ToList();

        var recentPays = pays
            .Select(p => new PetHistoryPaymentItemDto(
                p.Id,
                p.PaidAtUtc,
                p.ClinicId,
                ClinicName(p.ClinicId),
                p.Amount,
                p.Currency,
                p.Method))
            .ToList();

        var dto = new PetHistorySummaryDto(
            pet.Id,
            pet.Name,
            pet.ClientId,
            clientName,
            recentAppts,
            recentExams,
            recentTreatments,
            recentRx,
            recentLabs,
            recentHosp,
            recentPays);

        return Result<PetHistorySummaryDto>.Success(dto);
    }
}
