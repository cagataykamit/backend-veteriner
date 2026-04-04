using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Treatments;
using MediatR;

namespace Backend.Veteriner.Application.Examinations.Queries.GetRelatedSummary;

public sealed class GetExaminationRelatedSummaryQueryHandler
    : IRequestHandler<GetExaminationRelatedSummaryQuery, Result<ExaminationRelatedSummaryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Treatment> _treatments;
    private readonly IReadRepository<Prescription> _prescriptions;
    private readonly IReadRepository<LabResult> _labResults;
    private readonly IReadRepository<Hospitalization> _hospitalizations;
    private readonly IReadRepository<Payment> _payments;

    public GetExaminationRelatedSummaryQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Examination> examinations,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IReadRepository<Clinic> clinics,
        IReadRepository<Treatment> treatments,
        IReadRepository<Prescription> prescriptions,
        IReadRepository<LabResult> labResults,
        IReadRepository<Hospitalization> hospitalizations,
        IReadRepository<Payment> payments)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _examinations = examinations;
        _pets = pets;
        _clients = clients;
        _clinics = clinics;
        _treatments = treatments;
        _prescriptions = prescriptions;
        _labResults = labResults;
        _hospitalizations = hospitalizations;
        _payments = payments;
    }

    public async Task<Result<ExaminationRelatedSummaryDto>> Handle(
        GetExaminationRelatedSummaryQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ExaminationRelatedSummaryDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var examinationId = request.Id;
        var e = await _examinations.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, examinationId), ct);
        if (e is null)
            return Result<ExaminationRelatedSummaryDto>.Failure("Examinations.NotFound", "Muayene kaydı bulunamadı.");
        if (_clinicContext.ClinicId is { } activeClinicId && e.ClinicId != activeClinicId)
            return Result<ExaminationRelatedSummaryDto>.Failure("Examinations.NotFound", "Muayene kaydı bulunamadı.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, e.PetId), ct);
        var petName = pet?.Name ?? string.Empty;
        var clientId = pet?.ClientId ?? Guid.Empty;
        var clientName = string.Empty;
        if (pet is not null)
        {
            var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, pet.ClientId), ct);
            clientName = client?.FullName ?? string.Empty;
        }

        var clinicId = _clinicContext.ClinicId;
        var take = ExaminationRelatedSummaryConstants.RelatedItemsTake;

        var treatments = await _treatments.ListAsync(
            new TreatmentsForExaminationRelatedSpec(tenantId, clinicId, examinationId, take), ct);
        var prescriptions = await _prescriptions.ListAsync(
            new PrescriptionsForExaminationRelatedSpec(tenantId, clinicId, examinationId, take), ct);
        var labs = await _labResults.ListAsync(
            new LabResultsForExaminationRelatedSpec(tenantId, clinicId, examinationId, take), ct);
        var hosp = await _hospitalizations.ListAsync(
            new HospitalizationsForExaminationRelatedSpec(tenantId, clinicId, examinationId, take), ct);
        var pays = await _payments.ListAsync(
            new PaymentsForExaminationRelatedSpec(tenantId, clinicId, examinationId, take), ct);

        var clinicIds = new HashSet<Guid>();
        foreach (var t in treatments) clinicIds.Add(t.ClinicId);
        foreach (var p in prescriptions) clinicIds.Add(p.ClinicId);
        foreach (var l in labs) clinicIds.Add(l.ClinicId);
        foreach (var h in hosp) clinicIds.Add(h.ClinicId);
        foreach (var pay in pays) clinicIds.Add(pay.ClinicId);

        var clinicIdArr = clinicIds.ToArray();
        var clinicRows = clinicIdArr.Length == 0
            ? []
            : await _clinics.ListAsync(new ClinicsByTenantIdsSpec(tenantId, clinicIdArr), ct);
        var clinicNameById = clinicRows.ToDictionary(x => x.Id, x => x.Name);

        string ClinicName(Guid id) => clinicNameById.GetValueOrDefault(id, string.Empty);

        var treatmentDtos = treatments
            .Select(t => new ExaminationRelatedTreatmentItemDto(
                t.Id,
                t.TreatmentDateUtc,
                t.ClinicId,
                ClinicName(t.ClinicId),
                t.Title))
            .ToList();

        var prescriptionDtos = prescriptions
            .Select(p => new ExaminationRelatedPrescriptionItemDto(
                p.Id,
                p.PrescribedAtUtc,
                p.ClinicId,
                ClinicName(p.ClinicId),
                p.Title,
                p.TreatmentId))
            .ToList();

        var labDtos = labs
            .Select(l => new ExaminationRelatedLabResultItemDto(
                l.Id,
                l.ResultDateUtc,
                l.ClinicId,
                ClinicName(l.ClinicId),
                l.TestName))
            .ToList();

        var hospDtos = hosp
            .Select(h => new ExaminationRelatedHospitalizationItemDto(
                h.Id,
                h.AdmittedAtUtc,
                h.ClinicId,
                ClinicName(h.ClinicId),
                h.Reason,
                h.DischargedAtUtc,
                h.DischargedAtUtc is null))
            .ToList();

        var payDtos = pays
            .Select(p => new ExaminationRelatedPaymentItemDto(
                p.Id,
                p.PaidAtUtc,
                p.ClinicId,
                ClinicName(p.ClinicId),
                p.Amount,
                p.Currency,
                p.Method))
            .ToList();

        var dto = new ExaminationRelatedSummaryDto(
            examinationId,
            e.PetId,
            petName,
            clientId,
            clientName,
            treatmentDtos,
            prescriptionDtos,
            labDtos,
            hospDtos,
            payDtos);

        return Result<ExaminationRelatedSummaryDto>.Success(dto);
    }
}
