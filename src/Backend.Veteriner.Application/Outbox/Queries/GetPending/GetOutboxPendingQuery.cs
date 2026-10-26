using Backend.Veteriner.Application.Common.Contracts.Outbox;
using MediatR;

namespace Backend.Veteriner.Application.Outbox.Queries.GetPending;

/// <summary>Bekleyen en eski 100 outbox mesajı (<c>GET /api/v1/outbox/pending</c>).</summary>
public sealed record GetOutboxPendingQuery : IRequest<IReadOnlyList<OutboxPendingMessageDto>>;

public sealed class GetOutboxPendingQueryHandler : IRequestHandler<GetOutboxPendingQuery, IReadOnlyList<OutboxPendingMessageDto>>
{
    private const int Take = 100;

    private readonly IOutboxAdminRepository _outbox;

    public GetOutboxPendingQueryHandler(IOutboxAdminRepository outbox) => _outbox = outbox;

    public Task<IReadOnlyList<OutboxPendingMessageDto>> Handle(GetOutboxPendingQuery request, CancellationToken ct)
        => _outbox.ListOldestPendingAsync(Take, ct);
}
