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

