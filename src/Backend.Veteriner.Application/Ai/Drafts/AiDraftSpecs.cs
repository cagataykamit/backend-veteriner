using Ardalis.Specification;
using Backend.Veteriner.Domain.Ai;

namespace Backend.Veteriner.Application.Ai.Drafts;

public sealed class AiDraftByIdSpec : Specification<AiDraft>
{
    public AiDraftByIdSpec(Guid tenantId, Guid draftId)
        => Query.Where(d => d.TenantId == tenantId && d.Id == draftId);
}

/// <summary>Bir kayıt için karar bekleyen (süresi geçmemiş) taslaklar, yeniden eskiye.</summary>
public sealed class PendingAiDraftsForTargetSpec : Specification<AiDraft>
{
    public const int MaxCount = 20;

    public PendingAiDraftsForTargetSpec(Guid tenantId, Guid clinicId, string targetType, Guid targetId, DateTime utcNow)
        => Query
            .Where(d => d.TenantId == tenantId
                        && d.ClinicId == clinicId
                        && d.TargetType == targetType
                        && d.TargetId == targetId
                        && d.Status == AiDraftStatus.Draft
                        && d.ExpiresAtUtc > utcNow)
            .OrderByDescending(d => d.CreatedAtUtc)
            .Take(MaxCount)
            .AsNoTracking();
}

/// <summary>Süre taraması: süresi geçmiş, karar bekleyen taslaklar (kiracılar arası, sistem kapsamında).</summary>
public sealed class DueAiDraftsSpec : Specification<AiDraft>
{
    public DueAiDraftsSpec(DateTime utcNow, int take)
        => Query
            .Where(d => d.Status == AiDraftStatus.Draft && d.ExpiresAtUtc <= utcNow)
            .OrderBy(d => d.ExpiresAtUtc)
            .Take(take);
}


/// <summary>Kayıtlara yazılmış (kabul edilmiş) taslaklar: "AI taslağından" izi (AI-010), eskiden yeniye.</summary>
public sealed class AcceptedAiDraftsForRecordsSpec : Specification<AiDraft>
{
    public AcceptedAiDraftsForRecordsSpec(Guid tenantId, IReadOnlyCollection<Guid> recordIds)
        => Query
            .Where(d => d.TenantId == tenantId
                        && d.AppliedRecordId != null
                        && recordIds.Contains(d.AppliedRecordId.Value)
                        && (d.Status == AiDraftStatus.Accepted || d.Status == AiDraftStatus.AcceptedWithEdits))
            .OrderBy(d => d.DecidedAtUtc)
            .AsNoTracking();
}
