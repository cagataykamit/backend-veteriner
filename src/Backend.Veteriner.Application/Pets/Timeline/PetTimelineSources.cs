using System.Linq.Expressions;
using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Application.Pets.Access;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Domain.Visits;

namespace Backend.Veteriner.Application.Pets.Timeline;

/// <summary>Geliş (Visit). Randevu ayrı satır olmaz; <c>AppointmentId</c> satıra bağlantı olarak katlanır.</summary>
internal sealed class VisitTimelineSource(IReadRepository<Visit> repository) : PetTimelineSourceBase<Visit>(repository)
{
    public override PetRecordKind Kind => PetRecordKind.Visit;

    protected override Expression<Func<Visit, DateTime>> EventAt => v => v.ArrivedAtUtc;

    protected override Expression<Func<Visit, Guid>> Id => v => v.Id;

    protected override Expression<Func<Visit, bool>> Filter(PetTimelineSourceQuery q)
        => q.IncludeVoided
            ? v => v.TenantId == q.TenantId && v.PetId == q.PetId
            : v => v.TenantId == q.TenantId && v.PetId == q.PetId && v.VoidedAtUtc == null;

    protected override ISpecificationBuilder<Visit> ApplyClinicScope(ISpecificationBuilder<Visit> builder, PetTimelineSourceQuery q)
        => PetVisitClinicScope.Apply(builder, q.SingleClinicId, q.AccessibleClinicIds);

    protected override PetTimelineRow ToRow(Visit v)
        => new(
            Kind,
            v.Id,
            v.ArrivedAtUtc,
            v.ClinicId,
            "Geliş",
            v.IsUrgent ? "Acil" : null,
            v.CareStatus.ToString(),
            v.IsVoided,
            RelatedExaminationId: null,
            RelatedVisitId: null,
            RelatedAppointmentId: v.AppointmentId,
            UpdatedAtUtc: null);
}

internal sealed class ExaminationTimelineSource(IReadRepository<Examination> repository)
    : PetTimelineSourceBase<Examination>(repository)
{
    public override PetRecordKind Kind => PetRecordKind.Examination;

    protected override Expression<Func<Examination, DateTime>> EventAt => e => e.ExaminedAtUtc;

    protected override Expression<Func<Examination, Guid>> Id => e => e.Id;

    protected override Expression<Func<Examination, bool>> Filter(PetTimelineSourceQuery q)
        => e => e.TenantId == q.TenantId && e.PetId == q.PetId;

    protected override ISpecificationBuilder<Examination> ApplyClinicScope(ISpecificationBuilder<Examination> builder, PetTimelineSourceQuery q)
        => DashboardSpecificationClinicScope.ApplyToExamination(builder, q.SingleClinicId, q.AccessibleClinicIds);

    protected override PetTimelineRow ToRow(Examination e)
        => new(
            Kind,
            e.Id,
            e.ExaminedAtUtc,
            e.ClinicId,
            e.VisitReason,
            Summary: null,
            Status: null,
            IsVoided: false,
            RelatedExaminationId: null,
            RelatedVisitId: e.VisitId,
            RelatedAppointmentId: null,
            UpdatedAtUtc: e.UpdatedAtUtc);
}

internal sealed class TreatmentTimelineSource(IReadRepository<Treatment> repository)
    : PetTimelineSourceBase<Treatment>(repository)
{
    public override PetRecordKind Kind => PetRecordKind.Treatment;

    protected override Expression<Func<Treatment, DateTime>> EventAt => t => t.TreatmentDateUtc;

    protected override Expression<Func<Treatment, Guid>> Id => t => t.Id;

    protected override Expression<Func<Treatment, bool>> Filter(PetTimelineSourceQuery q)
        => t => t.TenantId == q.TenantId && t.PetId == q.PetId;

    protected override ISpecificationBuilder<Treatment> ApplyClinicScope(ISpecificationBuilder<Treatment> builder, PetTimelineSourceQuery q)
        => DashboardSpecificationClinicScope.ApplyToTreatment(builder, q.SingleClinicId, q.AccessibleClinicIds);

    protected override PetTimelineRow ToRow(Treatment t)
        => new(
            Kind,
            t.Id,
            t.TreatmentDateUtc,
            t.ClinicId,
            t.Title,
            Summary: null,
            Status: null,
            IsVoided: false,
            RelatedExaminationId: t.ExaminationId,
            RelatedVisitId: null,
            RelatedAppointmentId: null,
            UpdatedAtUtc: t.UpdatedAtUtc);
}

internal sealed class PrescriptionTimelineSource(IReadRepository<Prescription> repository)
    : PetTimelineSourceBase<Prescription>(repository)
{
    public override PetRecordKind Kind => PetRecordKind.Prescription;

    protected override Expression<Func<Prescription, DateTime>> EventAt => p => p.PrescribedAtUtc;

    protected override Expression<Func<Prescription, Guid>> Id => p => p.Id;

    protected override Expression<Func<Prescription, bool>> Filter(PetTimelineSourceQuery q)
        => p => p.TenantId == q.TenantId && p.PetId == q.PetId;

    protected override ISpecificationBuilder<Prescription> ApplyClinicScope(ISpecificationBuilder<Prescription> builder, PetTimelineSourceQuery q)
        => DashboardSpecificationClinicScope.ApplyToPrescription(builder, q.SingleClinicId, q.AccessibleClinicIds);

    protected override PetTimelineRow ToRow(Prescription p)
        => new(
            Kind,
            p.Id,
            p.PrescribedAtUtc,
            p.ClinicId,
            p.Title,
            Summary: null,
            Status: null,
            IsVoided: false,
            RelatedExaminationId: p.ExaminationId,
            RelatedVisitId: null,
            RelatedAppointmentId: null,
            UpdatedAtUtc: p.UpdatedAtUtc);
}

internal sealed class LabResultTimelineSource(IReadRepository<LabResult> repository)
    : PetTimelineSourceBase<LabResult>(repository)
{
    public override PetRecordKind Kind => PetRecordKind.LabResult;

    protected override Expression<Func<LabResult, DateTime>> EventAt => l => l.RequestedAtUtc;

    protected override Expression<Func<LabResult, Guid>> Id => l => l.Id;

    protected override Expression<Func<LabResult, bool>> Filter(PetTimelineSourceQuery q)
        => q.IncludeVoided
            ? l => l.TenantId == q.TenantId && l.PetId == q.PetId
            : l => l.TenantId == q.TenantId && l.PetId == q.PetId && l.Status != LabResultStatus.Cancelled;

    protected override ISpecificationBuilder<LabResult> ApplyClinicScope(ISpecificationBuilder<LabResult> builder, PetTimelineSourceQuery q)
        => DashboardSpecificationClinicScope.ApplyToLabResult(builder, q.SingleClinicId, q.AccessibleClinicIds);

    protected override PetTimelineRow ToRow(LabResult l)
        => new(
            Kind,
            l.Id,
            l.RequestedAtUtc,
            l.ClinicId,
            l.TestName,
            Summary: null,
            Status: l.Status.ToString(),
            IsVoided: l.Status == LabResultStatus.Cancelled,
            RelatedExaminationId: l.ExaminationId,
            RelatedVisitId: l.VisitId,
            RelatedAppointmentId: null,
            UpdatedAtUtc: l.UpdatedAtUtc);
}

/// <summary>
/// Uygulanan aşı <c>AppliedAtUtc</c>'te olaydır. Planlı aşı timeline'a girmez (özetteki "yaklaşan"dır);
/// iptal edilmiş aşı yalnız <c>includeVoided</c> ile gelir ve <c>DueAtUtc ?? CreatedAtUtc</c>'te durur.
/// </summary>
internal sealed class VaccinationTimelineSource(IReadRepository<Vaccination> repository)
    : PetTimelineSourceBase<Vaccination>(repository)
{
    public override PetRecordKind Kind => PetRecordKind.Vaccination;

    protected override Expression<Func<Vaccination, DateTime>> EventAt
        => v => v.Status == VaccinationStatus.Applied
            ? (v.AppliedAtUtc ?? v.CreatedAtUtc)
            : (v.DueAtUtc ?? v.CreatedAtUtc);

    protected override Expression<Func<Vaccination, Guid>> Id => v => v.Id;

    protected override Expression<Func<Vaccination, bool>> Filter(PetTimelineSourceQuery q)
        => q.IncludeVoided
            ? v => v.TenantId == q.TenantId && v.PetId == q.PetId && v.Status != VaccinationStatus.Scheduled
            : v => v.TenantId == q.TenantId && v.PetId == q.PetId && v.Status == VaccinationStatus.Applied;

    protected override ISpecificationBuilder<Vaccination> ApplyClinicScope(ISpecificationBuilder<Vaccination> builder, PetTimelineSourceQuery q)
        => DashboardSpecificationClinicScope.ApplyToVaccination(builder, q.SingleClinicId, q.AccessibleClinicIds);

    protected override PetTimelineRow ToRow(Vaccination v)
    {
        var applied = v.Status == VaccinationStatus.Applied;
        return new PetTimelineRow(
            Kind,
            v.Id,
            applied ? v.AppliedAtUtc ?? v.CreatedAtUtc : v.DueAtUtc ?? v.CreatedAtUtc,
            v.ClinicId,
            v.VaccineName,
            Summary: null,
            Status: v.Status.ToString(),
            IsVoided: !applied,
            RelatedExaminationId: v.ExaminationId,
            RelatedVisitId: null,
            RelatedAppointmentId: null,
            UpdatedAtUtc: v.UpdatedAtUtc);
    }
}

/// <summary>Yatış tek olaydır (kabul tarihinde); taburcu <c>Status</c> ile ifade edilir.</summary>
internal sealed class HospitalizationTimelineSource(IReadRepository<Hospitalization> repository)
    : PetTimelineSourceBase<Hospitalization>(repository)
{
    public override PetRecordKind Kind => PetRecordKind.Hospitalization;

    protected override Expression<Func<Hospitalization, DateTime>> EventAt => h => h.AdmittedAtUtc;

    protected override Expression<Func<Hospitalization, Guid>> Id => h => h.Id;

    protected override Expression<Func<Hospitalization, bool>> Filter(PetTimelineSourceQuery q)
        => h => h.TenantId == q.TenantId && h.PetId == q.PetId;

    protected override ISpecificationBuilder<Hospitalization> ApplyClinicScope(ISpecificationBuilder<Hospitalization> builder, PetTimelineSourceQuery q)
        => DashboardSpecificationClinicScope.ApplyToHospitalization(builder, q.SingleClinicId, q.AccessibleClinicIds);

    protected override PetTimelineRow ToRow(Hospitalization h)
        => new(
            Kind,
            h.Id,
            h.AdmittedAtUtc,
            h.ClinicId,
            h.Reason,
            Summary: null,
            Status: h.DischargedAtUtc is null ? "Active" : "Discharged",
            IsVoided: false,
            RelatedExaminationId: h.ExaminationId,
            RelatedVisitId: null,
            RelatedAppointmentId: null,
            UpdatedAtUtc: h.UpdatedAtUtc);
}
