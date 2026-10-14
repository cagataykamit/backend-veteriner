using Ardalis.Specification;
using Backend.Veteriner.Domain.ChargeSuggestions;

namespace Backend.Veteriner.Application.ChargeSuggestions.Specs;

/// <summary>İzlenen (tracked) kalem; çevirme ve ücretsiz sayma için.</summary>
public sealed class ChargeSuggestionForUpdateSpec : Specification<ChargeSuggestion>
{
    public ChargeSuggestionForUpdateSpec(Guid tenantId, Guid id)
    {
        Query.Where(s => s.TenantId == tenantId && s.Id == id);
    }
}

/// <summary>Kalemin veritabanındaki güncel hâli (eşzamanlı karar sonrası yeniden okuma).</summary>
public sealed class ChargeSuggestionCurrentSpec : Specification<ChargeSuggestion>
{
    public ChargeSuggestionCurrentSpec(Guid tenantId, Guid id)
    {
        Query.AsNoTracking();
        Query.Where(s => s.TenantId == tenantId && s.Id == id);
    }
}

/// <summary>İşlemden doğan kalem (izlenen); kaynak iptalinde kalemi düşürmek için.</summary>
public sealed class ChargeSuggestionBySourceForUpdateSpec : Specification<ChargeSuggestion>
{
    public ChargeSuggestionBySourceForUpdateSpec(Guid tenantId, string sourceType, Guid sourceId)
    {
        Query.Where(s => s.TenantId == tenantId && s.SourceType == sourceType && s.SourceId == sourceId);
    }
}

/// <summary>Klinik kapsamlı bekleyen kalemler; sayfalama <see cref="PendingChargeSuggestionsPagedSpec"/> ile eklenir.
/// <c>petIds</c> verilirse yalnız bu hayvanların kalemleri (müşteri süzgeci: müşterinin güncel hayvanları).</summary>
public class PendingChargeSuggestionsSpec : Specification<ChargeSuggestion>
{
    public PendingChargeSuggestionsSpec(
        Guid tenantId, Guid clinicId, Guid? petId, Guid? examinationId, Guid? visitId, IReadOnlyCollection<Guid>? petIds = null)
    {
        Query.AsNoTracking();
        Query.Where(s => s.TenantId == tenantId && s.ClinicId == clinicId && s.Status == ChargeSuggestionStatus.Pending);
        if (petId.HasValue)
            Query.Where(s => s.PetId == petId.Value);
        if (examinationId.HasValue)
            Query.Where(s => s.ExaminationId == examinationId.Value);
        if (visitId.HasValue)
            Query.Where(s => s.VisitId == visitId.Value);
        if (petIds is not null)
            Query.Where(s => petIds.Contains(s.PetId));
    }
}

public sealed class PendingChargeSuggestionsPagedSpec : PendingChargeSuggestionsSpec
{
    public PendingChargeSuggestionsPagedSpec(
        Guid tenantId, Guid clinicId, Guid? petId, Guid? examinationId, Guid? visitId, int page, int pageSize,
        IReadOnlyCollection<Guid>? petIds = null)
        : base(tenantId, clinicId, petId, examinationId, visitId, petIds)
    {
        Query.OrderByDescending(s => s.OccurredAtUtc)
            .ThenByDescending(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}

/// <summary>Muayeneye bağlı bekleyen kalem sayısı için (klinik bağlamı varsa onunla sınırlı).</summary>
public sealed class PendingChargeSuggestionsForExaminationSpec : Specification<ChargeSuggestion>
{
    public PendingChargeSuggestionsForExaminationSpec(Guid tenantId, Guid? clinicId, Guid examinationId)
    {
        Query.AsNoTracking();
        Query.Where(s => s.TenantId == tenantId
                         && s.ExaminationId == examinationId
                         && s.Status == ChargeSuggestionStatus.Pending);
        if (clinicId.HasValue)
            Query.Where(s => s.ClinicId == clinicId.Value);
    }
}

/// <summary>Bugün satırları için geliş bazında bekleyen kalemlerin geliş kimlikleri (sayım bellekte).</summary>
public sealed class PendingChargeSuggestionVisitIdsSpec : Specification<ChargeSuggestion, Guid>
{
    public PendingChargeSuggestionVisitIdsSpec(Guid tenantId, Guid clinicId, IReadOnlyCollection<Guid> visitIds)
    {
        Query.AsNoTracking();
        Query.Where(s => s.TenantId == tenantId
                         && s.ClinicId == clinicId
                         && s.Status == ChargeSuggestionStatus.Pending
                         && s.VisitId.HasValue
                         && visitIds.Contains(s.VisitId.Value));
        Query.Select(s => s.VisitId!.Value);
    }
}
