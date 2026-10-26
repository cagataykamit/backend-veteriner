using Backend.Veteriner.Application.Common.Contracts.Outbox;
using Backend.Veteriner.Application.Common.Models;
using Mediator;

namespace Backend.Veteriner.Application.Outbox.Queries.GetDeadLettersPaged;

/// <summary>Yönetim: dead-letter outbox mesajları, sayfalı (<c>GET /api/v1/admin/outbox/dead</c>).</summary>
public sealed record GetOutboxDeadLettersPagedQuery(PageRequest PageRequest) : IRequest<PagedResult<OutboxItemDto>>;

public sealed class GetOutboxDeadLettersPagedQueryHandler
    : IRequestHandler<GetOutboxDeadLettersPagedQuery, PagedResult<OutboxItemDto>>
{
    private readonly IOutboxAdminRepository _outbox;

    public GetOutboxDeadLettersPagedQueryHandler(IOutboxAdminRepository outbox) => _outbox = outbox;

    public ValueTask<PagedResult<OutboxItemDto>> Handle(GetOutboxDeadLettersPagedQuery request, CancellationToken ct)
        => new(_outbox.GetDeadLettersPagedAsync(request.PageRequest, ct));
}
