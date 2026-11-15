using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Public.TrialSignup;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Public;

/// <summary>Deneme kaydı testleri: belleğe yazan depolar, açık özet (girdinin kendisi), doğrulanmış e-posta satırı kurulumu.</summary>
internal sealed class TrialSignupTestSupport
{
    public const string Email = "owner@a.com";
    public const string Token = "test-jetonu";
    public const string ValidVkn = "1234567890";
    public const string ValidTckn = "10000000146";

    public List<SignupEmailVerification> Verifications { get; } = new();
    public List<TrialSignupIdentity> Identities { get; } = new();
    public Mock<IRepository<SignupEmailVerification>> VerificationRepository { get; }
    public Mock<IRepository<TrialSignupIdentity>> IdentityRepository { get; }
    public Mock<IReadRepository<User>> Users { get; } = new();
    public Mock<IEmailSender> EmailSender { get; } = new();
    public Mock<IOwnerPortalSecretGenerator> Secrets { get; } = new();
    public Mock<ITokenHashService> Hash { get; } = new();
    public TrialSignupOptions Options { get; set; } = new();

    public TrialSignupTestSupport()
    {
        VerificationRepository = InMemoryOver(Verifications);
        IdentityRepository = InMemoryOver(Identities);
        Hash.Setup(h => h.ComputeSha256(It.IsAny<string>())).Returns<string>(s => s);
        Secrets.Setup(s => s.CreateNumericCode(SignupEmailVerification.CodeLength)).Returns("123456");
        Secrets.Setup(s => s.CreateToken()).Returns(Token);
    }

    public SignupEmailVerificationService CreateEmailService()
        => new(VerificationRepository.Object, EmailSender.Object, Secrets.Object, Hash.Object,
            NullLogger<SignupEmailVerificationService>.Instance);

    public TrialSignupIdentityService CreateIdentityService()
        => new(IdentityRepository.Object, Users.Object, Microsoft.Extensions.Options.Options.Create(Options));

    /// <summary><paramref name="email"/> için kodu doğrulanmış, jetonu <see cref="Token"/> olan satır ekler.</summary>
    public SignupEmailVerification AddVerified(DateTime utcNow, string email = Email)
    {
        var id = Guid.NewGuid();
        var row = SignupEmailVerification.Issue(
            id, email, SignupEmailVerificationService.ComputeSecretHash(Hash.Object, id, SignupEmailVerificationService.CodePurpose, "123456"),
            null, utcNow);
        row.TryVerify(true, SignupEmailVerificationService.ComputeSecretHash(Hash.Object, id, SignupEmailVerificationService.TokenPurpose, Token), utcNow);
        Verifications.Add(row);
        return row;
    }

    private static Mock<IRepository<T>> InMemoryOver<T>(List<T> rows) where T : class
    {
        var repo = new Mock<IRepository<T>>();
        repo.Setup(r => r.AnyAsync(It.IsAny<ISpecification<T>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISpecification<T> s, CancellationToken _) => s.Evaluate(rows).Any());
        repo.Setup(r => r.CountAsync(It.IsAny<ISpecification<T>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISpecification<T> s, CancellationToken _) => s.Evaluate(rows).Count());
        repo.Setup(r => r.ListAsync(It.IsAny<ISpecification<T>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISpecification<T> s, CancellationToken _) => s.Evaluate(rows).ToList());
        repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<T>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISpecification<T> s, CancellationToken _) => s.Evaluate(rows).FirstOrDefault());
        repo.Setup(r => r.AddAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()))
            .Callback<T, CancellationToken>((v, _) => rows.Add(v))
            .ReturnsAsync((T v, CancellationToken _) => v);
        return repo;
    }
}
