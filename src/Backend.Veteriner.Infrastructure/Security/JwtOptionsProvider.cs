using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Security;

public sealed class JwtOptionsProvider : IJwtOptionsProvider
{
    private readonly JwtOptions _opt;
    public JwtOptionsProvider(IOptions<JwtOptions> opt) => _opt = opt.Value;
    public int AccessTokenMinutes => _opt.ExpMinutes;
    public int RefreshTokenDays => _opt.RefreshExpDays;
}
