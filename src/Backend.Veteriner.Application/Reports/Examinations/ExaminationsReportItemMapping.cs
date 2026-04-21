using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Reports.Examinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Reports.Examinations;

internal static class ExaminationsReportItemMapping
{
    public static async Task<IReadOnlyList<ExaminationReportItemDto>> MapAsync(
        Guid tenantId,
        IReadOnlyList<Examination> rows,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];

        var petIds = rows.Select(x => x.PetId).Distinct().ToArray();
        var petRows = await pets.ListAsync(new PetsByTenantIdsNameClientSpec(tenantId, petIds), ct);
        var petById = petRows.ToDictionary(x => x.Id);

        var clientIds = petRows.Select(x => x.ClientId).Distinct().ToArray();
        var clientRows = await clients.ListAsync(new ClientsByTenantIdsNameSpec(tenantId, clientIds), ct);
        var clientNameById = clientRows.ToDictionary(x => x.Id, x => x.FullName);

        var clinicIds = rows.Select(x => x.ClinicId).Distinct().ToArray();
        var clinicRows = await clinics.ListAsync(new ClinicsByTenantIdsNameSpec(tenantId, clinicIds), ct);
        var clinicNameById = clinicRows.ToDictionary(x => x.Id, x => x.Name);

        return rows
            .Select(e =>
            {
                petById.TryGetValue(e.PetId, out var pet);
                var clientId = pet?.ClientId ?? Guid.Empty;
                var clientName = clientId != Guid.Empty && clientNameById.TryGetValue(clientId, out var cn)
                    ? (string.IsNullOrWhiteSpace(cn) ? string.Empty : cn.Trim())
                    : string.Empty;
                var petName = string.IsNullOrWhiteSpace(pet?.Name) ? string.Empty : pet.Name.Trim();
                var clinicNameRaw = clinicNameById.GetValueOrDefault(e.ClinicId, string.Empty);
                var clinicName = string.IsNullOrWhiteSpace(clinicNameRaw) ? string.Empty : clinicNameRaw.Trim();

                return new ExaminationReportItemDto(
                    e.Id,
                    e.ExaminedAtUtc,
                    e.ClinicId,
                    clinicName,
                    clientId,
                    clientName,
                    e.PetId,
                    petName,
                    e.AppointmentId,
                    e.VisitReason,
                    e.Findings,
                    e.Assessment,
                    e.Notes);
            })
            .ToList();
    }
}
