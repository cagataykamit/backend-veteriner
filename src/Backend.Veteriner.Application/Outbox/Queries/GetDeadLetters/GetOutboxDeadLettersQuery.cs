using Backend.Veteriner.Application.Common.Contracts.Outbox;
using Mediator;

namespace Backend.Veteriner.Application.Outbox.Queries.GetDeadLetters;

/// <summary>En son düşen 100 dead-letter outbox mesajı (<c>GET /api/v1/outbox/dead</c>).</summary>
public sealed record GetOutboxDeadLettersQuery : IRequest<IReadOnlyList<OutboxDeadLetterMessageDto>>;

public sealed class GetOutboxDeadLettersQueryHandler
    : IRequestHandler<GetOutboxDeadLettersQuery, IReadOnlyList<OutboxDeadLetterMessageDto>>
{
    private const int Take = 100;

    private readonly IOutboxAdminRepository _outbox;

    public GetOutboxDeadLettersQueryHandler(IOutboxAdminRepository outbox) => _outbox = outbox;

    public ValueTask<IReadOnlyList<OutboxDeadLetterMessageDto>> Handle(GetOutboxDeadLettersQuery request, CancellationToken ct)
        => new(_outbox.ListLatestDeadLettersAsync(Take, ct));
}
