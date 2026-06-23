using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Dashboard;

/// <summary>Dashboard ve child summary spec'lerinde tekrarlanan klinik kapsam filtresi.</summary>
internal static class DashboardSpecificationClinicScope
{
    internal static ISpecificationBuilder<Appointment> ApplyToAppointment(
        ISpecificationBuilder<Appointment> query,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        if (clinicId.HasValue)
            return query.Where(a => a.ClinicId == clinicId.Value);

        if (accessibleClinicIds is null)
            return query;

        if (accessibleClinicIds.Count == 0)
            return query.Where(_ => false);

        return query.Where(a => accessibleClinicIds.Contains(a.ClinicId));
    }

    internal static ISpecificationBuilder<Vaccination> ApplyToVaccination(
        ISpecificationBuilder<Vaccination> query,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        if (clinicId.HasValue)
            return query.Where(v => v.ClinicId == clinicId.Value);

        if (accessibleClinicIds is null)
            return query;

        if (accessibleClinicIds.Count == 0)
            return query.Where(_ => false);

        return query.Where(v => accessibleClinicIds.Contains(v.ClinicId));
    }

    internal static ISpecificationBuilder<Payment> ApplyToPayment(
        ISpecificationBuilder<Payment> query,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        if (clinicId.HasValue)
            return query.Where(p => p.ClinicId == clinicId.Value);

        if (accessibleClinicIds is null)
            return query;

        if (accessibleClinicIds.Count == 0)
            return query.Where(_ => false);

        return query.Where(p => accessibleClinicIds.Contains(p.ClinicId));
    }

    internal static ISpecificationBuilder<Examination> ApplyToExamination(
        ISpecificationBuilder<Examination> query,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        if (clinicId.HasValue)
            return query.Where(e => e.ClinicId == clinicId.Value);

        if (accessibleClinicIds is null)
            return query;

        if (accessibleClinicIds.Count == 0)
            return query.Where(_ => false);

        return query.Where(e => accessibleClinicIds.Contains(e.ClinicId));
    }

    internal static ISpecificationBuilder<Treatment> ApplyToTreatment(
        ISpecificationBuilder<Treatment> query,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        if (clinicId.HasValue)
            return query.Where(t => t.ClinicId == clinicId.Value);

        if (accessibleClinicIds is null)
            return query;

        if (accessibleClinicIds.Count == 0)
            return query.Where(_ => false);

        return query.Where(t => accessibleClinicIds.Contains(t.ClinicId));
    }

    internal static ISpecificationBuilder<Prescription> ApplyToPrescription(
        ISpecificationBuilder<Prescription> query,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        if (clinicId.HasValue)
            return query.Where(p => p.ClinicId == clinicId.Value);

        if (accessibleClinicIds is null)
            return query;

        if (accessibleClinicIds.Count == 0)
            return query.Where(_ => false);

        return query.Where(p => accessibleClinicIds.Contains(p.ClinicId));
    }

    internal static ISpecificationBuilder<LabResult> ApplyToLabResult(
        ISpecificationBuilder<LabResult> query,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        if (clinicId.HasValue)
            return query.Where(l => l.ClinicId == clinicId.Value);

        if (accessibleClinicIds is null)
            return query;

        if (accessibleClinicIds.Count == 0)
            return query.Where(_ => false);

        return query.Where(l => accessibleClinicIds.Contains(l.ClinicId));
    }

    internal static ISpecificationBuilder<Hospitalization> ApplyToHospitalization(
        ISpecificationBuilder<Hospitalization> query,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        if (clinicId.HasValue)
            return query.Where(h => h.ClinicId == clinicId.Value);

        if (accessibleClinicIds is null)
            return query;

        if (accessibleClinicIds.Count == 0)
            return query.Where(_ => false);

        return query.Where(h => accessibleClinicIds.Contains(h.ClinicId));
    }
}
