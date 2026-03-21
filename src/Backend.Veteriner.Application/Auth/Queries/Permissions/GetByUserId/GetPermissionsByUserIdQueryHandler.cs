using Backend.Veteriner.Application.Auth.Contracts;
using Backend.Veteriner.Application.Auth.Queries.Permissions.GetByUserId;
using MediatR;

public sealed class GetPermissionsByUserIdQueryHandler
    : IRequestHandler<GetPermissionsByUserIdQuery, IReadOnlyList<string>>
{
    private readonly IPermissionReader _reader;
    public GetPermissionsByUserIdQueryHandler(IPermissionReader reader) => _reader = reader;

    public Task<IReadOnlyList<string>> Handle(GetPermissionsByUserIdQuery q, CancellationToken ct)
        => _reader.GetPermissionsAsync(q.UserId, principal: null, ct); // principal olmadan da okuyabilelim
}
