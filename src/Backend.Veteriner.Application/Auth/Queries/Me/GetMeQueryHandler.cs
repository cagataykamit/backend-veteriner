using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Users.Specs;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Queries.Me;

public sealed class GetMeQueryHandler : IRequestHandler<GetMeQuery, MeDto>
{
    private readonly IUserReadRepository _users;

    public GetMeQueryHandler(IUserReadRepository users) => _users = users;

    public async Task<MeDto> Handle(GetMeQuery request, CancellationToken ct)
    {
        var user = await _users.FirstOrDefaultAsync(new UserByIdWithRolesSpec(request.UserId), ct)
                   ?? throw new UnauthorizedAccessException("Kullan�c� bulunamad�.");

        var roles = user.Roles.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        return new MeDto(
            user.Id,
            user.Email,
            user.EmailConfirmed,   // ?? DB�den geliyor
            roles
        );
    }
}
