using Ardalis.Specification;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ClientCredits;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Imports.Specs;

// Güncelleme aktarması (ADR-051): kaynak kimliği bağları ve eşleşen müşterinin net bakiyesi; her biri tek sorgu.

/// <summary>Partinin kliniği ve kaynak programında, verilen kaynak anahtarlarının bağları (bir kayıt türü).</summary>
public sealed class ImportSourceLinksSpec : Specification<ImportSourceLink>
{
    public ImportSourceLinksSpec(
        Guid tenantId,
        Guid clinicId,
        ImportSourceProfile profile,
        ImportSourceEntity entity,
        IReadOnlyCollection<string> keys,
        bool tracking = false)
    {
        var keySet = keys.ToArray();
        if (!tracking)
            Query.AsNoTracking();
        Query.Where(l => l.TenantId == tenantId
                         && l.ClinicId == clinicId
                         && l.SourceProfile == profile
                         && l.Entity == entity
                         && keySet.Contains(l.SourceKey));
    }
}

/// <summary>Kaynak kimliği bağının hedeflediği müşteriler (kimlikle; peşin müşteri hariç, <see cref="ImportExistingClientsSpec"/> ile aynı biçim).</summary>
public sealed class ImportExistingClientsByIdsSpec : Specification<Client, ImportExistingClient>
{
    public ImportExistingClientsByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        var idSet = ids.ToArray();
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && !c.IsWalkIn && idSet.Contains(c.Id));
        Query.Select(c => new ImportExistingClient(c.Id, c.FullName, c.PhoneNormalized, c.Email));
    }
}

/// <summary>Kaynak kimliği bağının hedeflediği hastalar (kimlikle).</summary>
public sealed class ImportExistingPetsByIdsSpec : Specification<Pet, ImportExistingPet>
{
    public ImportExistingPetsByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        var idSet = ids.ToArray();
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && idSet.Contains(p.Id));
        Query.Select(p => new ImportExistingPet(p.Id, p.ClientId, p.Name, p.SpeciesId, p.MicrochipNumber, p.BirthDate));
    }
}

/// <summary>Müşteri bakiyesinin bir parçası (artı borç, eksi alacak).</summary>
public sealed record ImportBalancePart(Guid ClientId, decimal Amount);

/// <summary>
/// Net bakiye parçaları (kural <c>ClientBalanceTotals</c> ile aynı, tek klinik): kalan ücret (ücret − tahsis; iptal edilmiş ücret sorgu
/// filtresiyle dışarıda). <see cref="UnallocatedPayments"/> bağlanmamış TRY ödemeyi, <see cref="OpenCredits"/> açık alacağı eksi verir.
/// </summary>
public sealed class ImportClientOutstandingChargesSpec : Specification<Charge, ImportBalancePart>
{
    public ImportClientOutstandingChargesSpec(Guid tenantId, Guid clinicId, IReadOnlyCollection<Guid> clientIds)
    {
        var clientSet = clientIds.ToArray();
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.ClinicId == clinicId && clientSet.Contains(c.ClientId) && c.AllocatedAmount < c.Amount);
        Query.Select(c => new ImportBalancePart(c.ClientId, c.Amount - c.AllocatedAmount));
    }
}

/// <inheritdoc cref="ImportClientOutstandingChargesSpec"/>
public sealed class ImportClientUnallocatedPaymentsSpec : Specification<Payment, ImportBalancePart>
{
    public ImportClientUnallocatedPaymentsSpec(Guid tenantId, Guid clinicId, IReadOnlyCollection<Guid> clientIds)
    {
        var clientSet = clientIds.ToArray();
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && p.ClinicId == clinicId && clientSet.Contains(p.ClientId)
                         && p.Currency == Charge.DefaultCurrency && p.AllocatedAmount + p.CreditedAmount < p.Amount);
        Query.Select(p => new ImportBalancePart(p.ClientId, -(p.Amount - p.AllocatedAmount - p.CreditedAmount)));
    }
}

/// <inheritdoc cref="ImportClientOutstandingChargesSpec"/>
public sealed class ImportClientOpenCreditsSpec : Specification<ClientCredit, ImportBalancePart>
{
    public ImportClientOpenCreditsSpec(Guid tenantId, Guid clinicId, IReadOnlyCollection<Guid> clientIds)
    {
        var clientSet = clientIds.ToArray();
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.ClinicId == clinicId && clientSet.Contains(c.ClientId)
                         && c.RefundedAtUtc == null && c.VoidedAtUtc == null && c.UsedAmount < c.Amount);
        Query.Select(c => new ImportBalancePart(c.ClientId, -(c.Amount - c.UsedAmount)));
    }
}
