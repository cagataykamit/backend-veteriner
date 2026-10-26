using Backend.Veteriner.Application.Common.Contracts.Outbox;
using Backend.Veteriner.Application.Common.Models;
using MediatR;

namespace Backend.Veteriner.Application.Outbox.Queries.GetPendingPaged;

/// <summary>Yönetim: bekleyen outbox mesajları, sayfalı (<c>GET /api/v1/admin/outbox/pending</c>).</summary>
public sealed record GetOutboxPendingPagedQuery(PageRequest PageRequest) : IRequest<PagedResult<OutboxItemDto>>;

public sealed class GetOutboxPendingPagedQueryHandler : IRequestHandler<GetOutboxPendingPagedQuery, PagedResult<OutboxItemDto>>
{
    private readonly IOutboxAdminRepository _outbox;

    public GetOutboxPendingPagedQueryHandler(IOutboxAdminRepository outbox) => _outbox = outbox;

    public Task<PagedResult<OutboxItemDto>> Handle(GetOutboxPendingPagedQuery request, CancellationToken ct)
        => _outbox.GetPendingPagedAsync(request.PageRequest, ct);
}
