using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.OwnerPortal.Sessions;
using Backend.Veteriner.Domain.OwnerPortal;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.OwnerPortal;

/// <summary>Portal handler testleri için ortak sahte bağımlılıklar (jeton/kod üretimi belirlenebilir).</summary>
internal sealed class OwnerPortalTestFixture
{
    public static readonly DateTime Now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    public Mock<ITokenHashService> Hash { get; } = new();
    public Mock<IOwnerPortalSecretGenerator> Secrets { get; } = new();
    public Mock<IOwnerPortalTokenService> Tokens { get; } = new();
    public Mock<IEmailSender> Email { get; } = new();
    public Mock<IRepository<OwnerPortalCode>> Codes { get; } = new();
    public Mock<IRepository<OwnerPortalRefreshToken>> RefreshTokens { get; } = new();
    public Mock<IUnitOfWork> Uow { get; } = new();
    public List<OwnerPortalCode> AddedCodes { get; } = [];
    public List<OwnerPortalRefreshToken> AddedRefreshTokens { get; } = [];
    public string NextCode { get; set; } = "123456";
    public string NextToken { get; set; } = "raw-token";

    public OwnerPortalTestFixture()
    {
        Hash.Setup(h => h.ComputeSha256(It.IsAny<string>())).Returns<string>(s => "h:" + s);
        Secrets.Setup(s => s.CreateNumericCode(It.IsAny<int>())).Returns(() => NextCode);
        Secrets.Setup(s => s.CreateToken()).Returns(() => NextToken);
        Tokens.Setup(t => t.CreateAccessToken(It.IsAny<OwnerAccount>())).Returns(("access", Now.AddMinutes(15)));
        Codes.Setup(r => r.AddAsync(It.IsAny<OwnerPortalCode>(), It.IsAny<CancellationToken>()))
            .Callback<OwnerPortalCode, CancellationToken>((c, _) => AddedCodes.Add(c))
            .ReturnsAsync((OwnerPortalCode c, CancellationToken _) => c);
        Codes.Setup(r => r.ListAsync(It.IsAny<Ardalis.Specification.ISpecification<OwnerPortalCode>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => AddedCodes.Where(c => c.IsActive(Now)).ToList());
        Codes.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Ardalis.Specification.ISpecification<OwnerPortalCode>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => AddedCodes.LastOrDefault());
        RefreshTokens.Setup(r => r.AddAsync(It.IsAny<OwnerPortalRefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<OwnerPortalRefreshToken, CancellationToken>((t, _) => AddedRefreshTokens.Add(t))
            .ReturnsAsync((OwnerPortalRefreshToken t, CancellationToken _) => t);
        RefreshTokens.Setup(r => r.ListAsync(It.IsAny<Ardalis.Specification.ISpecification<OwnerPortalRefreshToken>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => AddedRefreshTokens.Where(t => !t.IsRevoked).ToList());
    }

    public FixedClock Clock { get; } = new(Now);

    public OwnerPortalCodeService CodeService() => new(Codes.Object, Secrets.Object, Hash.Object, Email.Object);

    public OwnerPortalSessionIssuer SessionIssuer() => new(
        Tokens.Object, Secrets.Object, Hash.Object, RefreshTokens.Object, Options.Create(new OwnerPortalOptions()));

    internal sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
