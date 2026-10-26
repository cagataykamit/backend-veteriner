using Backend.Veteriner.Application.Auth.Contracts;
using Backend.Veteriner.Application.Auth.Queries.Permissions.GetByUserId;
using Mediator;

public sealed class GetPermissionsByUserIdQueryHandler
    : IRequestHandler<GetPermissionsByUserIdQuery, IReadOnlyList<string>>
{
    private readonly IPermissionReader _reader;
    public GetPermissionsByUserIdQueryHandler(IPermissionReader reader) => _reader = reader;

    public ValueTask<IReadOnlyList<string>> Handle(GetPermissionsByUserIdQuery q, CancellationToken ct)
        => new(_reader.GetPermissionsAsync(q.UserId, principal: null, ct)); // principal olmadan da okuyabilelim
}
