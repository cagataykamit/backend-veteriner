using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Public.PhoneVerification;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Public;

/// <summary>Telefon doğrulaması testleri: belleğe yazan depo, açık özet (girdinin kendisi) ve doğrulanmış satır kurulumu.</summary>
internal sealed class SignupPhoneVerificationTestSupport
{
    public const string Phone = "905321234567";
    public const string Token = "test-jetonu";

    public List<SignupPhoneVerification> Rows { get; } = new();
    public Mock<IRepository<SignupPhoneVerification>> Repository { get; } = new();
    public Mock<ISmsSender> Sms { get; } = new();
    public Mock<IOwnerPortalSecretGenerator> Secrets { get; } = new();
    public Mock<ITokenHashService> Hash { get; } = new();

    public SignupPhoneVerificationTestSupport()
    {
        Hash.Setup(h => h.ComputeSha256(It.IsAny<string>())).Returns<string>(s => s);
        Repository.Setup(r => r.AnyAsync(It.IsAny<ISpecification<SignupPhoneVerification>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISpecification<SignupPhoneVerification> s, CancellationToken _) => s.Evaluate(Rows).Any());
        Repository.Setup(r => r.CountAsync(It.IsAny<ISpecification<SignupPhoneVerification>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISpecification<SignupPhoneVerification> s, CancellationToken _) => s.Evaluate(Rows).Count());
        Repository.Setup(r => r.ListAsync(It.IsAny<ISpecification<SignupPhoneVerification>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISpecification<SignupPhoneVerification> s, CancellationToken _) => s.Evaluate(Rows).ToList());
        Repository.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<SignupPhoneVerification>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISpecification<SignupPhoneVerification> s, CancellationToken _) => s.Evaluate(Rows).FirstOrDefault());
        Repository.Setup(r => r.AddAsync(It.IsAny<SignupPhoneVerification>(), It.IsAny<CancellationToken>()))
            .Callback<SignupPhoneVerification, CancellationToken>((v, _) => Rows.Add(v))
            .ReturnsAsync((SignupPhoneVerification v, CancellationToken _) => v);
        Sms.SetupGet(s => s.IsConfigured).Returns(true);
        Sms.Setup(s => s.SendOneTimeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SmsSendResult.Success("job"));
        Secrets.Setup(s => s.CreateNumericCode(SignupPhoneVerification.CodeLength)).Returns("123456");
        Secrets.Setup(s => s.CreateToken()).Returns(Token);
    }

    public SignupPhoneVerificationService CreateService()
        => new(Repository.Object, Sms.Object, Secrets.Object, Hash.Object, NullLogger<SignupPhoneVerificationService>.Instance);

    /// <summary><paramref name="phone"/> için kodu doğrulanmış, jetonu <see cref="Token"/> olan satır ekler.</summary>
    public SignupPhoneVerification AddVerified(DateTime utcNow, string phone = Phone)
    {
        var id = Guid.NewGuid();
        var row = SignupPhoneVerification.Issue(
            id, phone, SignupPhoneVerificationService.ComputeSecretHash(Hash.Object, id, SignupPhoneVerificationService.CodePurpose, "123456"),
            null, utcNow);
        row.TryVerify(true, SignupPhoneVerificationService.ComputeSecretHash(Hash.Object, id, SignupPhoneVerificationService.TokenPurpose, Token), utcNow);
        Rows.Add(row);
        return row;
    }
}
