using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Dashboard;

/// <summary>Dashboard appointment/vaccination spec'lerinde tekrarlanan klinik kapsam filtresi.</summary>
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
}
