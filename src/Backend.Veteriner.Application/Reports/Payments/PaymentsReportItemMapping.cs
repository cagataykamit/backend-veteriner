using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Reports.Payments;

internal static class PaymentsReportItemMapping
{
    public static async Task<IReadOnlyList<PaymentReportItemDto>> MapAsync(
        Guid tenantId,
        IReadOnlyList<Payment> rows,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];

        var clientIds = rows.Select(x => x.ClientId).Distinct().ToArray();
        var clientRows = await clients.ListAsync(new ClientsByTenantIdsSpec(tenantId, clientIds), ct);
        var clientNameById = clientRows.ToDictionary(x => x.Id, x => x.FullName);

        var petIds = rows.Where(x => x.PetId.HasValue).Select(x => x.PetId!.Value).Distinct().ToArray();
        var petRows = petIds.Length == 0
            ? []
            : await pets.ListAsync(new PetsByTenantIdsSpec(tenantId, petIds), ct);
        var petById = petRows.ToDictionary(x => x.Id);

        var clinicIds = rows.Select(x => x.ClinicId).Distinct().ToArray();
        var clinicRows = await clinics.ListAsync(new ClinicsByTenantIdsSpec(tenantId, clinicIds), ct);
        var clinicNameById = clinicRows.ToDictionary(x => x.Id, x => x.Name);

        return rows
            .Select(p =>
            {
                var clientName = clientNameById.TryGetValue(p.ClientId, out var cn) ? cn : string.Empty;
                string petName = string.Empty;
                if (p.PetId is { } pid && petById.TryGetValue(pid, out var pet))
                    petName = pet.Name;

                var clinicName = clinicNameById.GetValueOrDefault(p.ClinicId, string.Empty);

                return new PaymentReportItemDto(
                    p.Id,
                    p.PaidAtUtc,
                    p.ClinicId,
                    clinicName,
                    p.ClientId,
                    clientName,
                    p.PetId,
                    petName,
                    p.Amount,
                    p.Currency,
                    p.Method,
                    p.Notes);
            })
            .ToList();
    }
}
