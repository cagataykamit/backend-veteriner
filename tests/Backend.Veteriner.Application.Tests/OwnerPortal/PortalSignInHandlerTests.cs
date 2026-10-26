using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Commands.PortalCodeSignIn;
using Backend.Veteriner.Application.OwnerPortal.Commands.PortalPasswordSignIn;
using Backend.Veteriner.Application.OwnerPortal.Commands.RequestPortalCode;
using Backend.Veteriner.Domain.OwnerPortal;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.OwnerPortal;

/// <summary>ADR-031 K3: kodla/parolayla giriş, deneme sayımı ve numaralandırmaya karşı aynı yanıt.</summary>
public sealed class PortalSignInHandlerTests
{
    private const string InvalidCredentials = "Portal.Auth.Unauthorized.InvalidCredentials";

    private readonly OwnerPortalTestFixture _f = new();
    private readonly Mock<IReadRepository<OwnerAccount>> _accounts = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private OwnerAccount? _account;

    public PortalSignInHandlerTests()
    {
        _accounts.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<OwnerAccount>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _account);
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => "bcrypt:" + p);
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns<string, string>((p, hash) => hash == "bcrypt:" + p);
    }

    private OwnerAccount GivenAccount(string? password = null)
    {
        _account = OwnerAccount.Create("ayse@example.com", "Ayşe", OwnerPortalTestFixture.Now);
        if (password is not null)
            _account.SetPasswordHash("bcrypt:" + password);
        return _account;
    }

    private RequestPortalCodeCommandHandler RequestHandler() => new(_accounts.Object, _f.CodeService(), _f.Clock);

    private PortalCodeSignInCommandHandler CodeHandler() =>
        new(_accounts.Object, _f.CodeService(), _f.SessionIssuer(), _f.Uow.Object, _f.Clock);

    private PortalPasswordSignInCommandHandler PasswordHandler() =>
        new(_accounts.Object, _hasher.Object, _f.SessionIssuer(), _f.Uow.Object, _f.Clock);

    [Fact]
    public async Task RequestCode_For_Unknown_Email_Should_Succeed_Without_Email()
    {
        var result = await RequestHandler().Handle(
            new RequestPortalCodeCommand("nobody@example.com", OwnerPortalCodePurpose.SignIn), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _f.Email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
        _f.AddedCodes.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestCode_Should_Email_Code_And_Store_Only_Hash()
    {
        GivenAccount();
        _f.NextCode = "654321";

        await RequestHandler().Handle(new RequestPortalCodeCommand("AYSE@example.com", OwnerPortalCodePurpose.SignIn), CancellationToken.None);

        _f.Email.Verify(e => e.SendAsync("ayse@example.com", It.IsAny<string>(), It.Is<string>(b => b.Contains("654321")), It.IsAny<CancellationToken>(), false), Times.Once);
        var code = _f.AddedCodes.Should().ContainSingle().Subject;
        code.CodeHash.Should().Be($"h:{code.Id:N}:654321", "özet kod satırı kimliğiyle tuzlanır (sahte özet servisi)");
    }

    [Fact]
    public async Task RequestCode_Within_Cooldown_Should_Not_Send_Second_Code()
    {
        GivenAccount();
        await RequestHandler().Handle(new RequestPortalCodeCommand("ayse@example.com", OwnerPortalCodePurpose.SignIn), CancellationToken.None);

        await RequestHandler().Handle(new RequestPortalCodeCommand("ayse@example.com", OwnerPortalCodePurpose.SignIn), CancellationToken.None);

        _f.AddedCodes.Should().HaveCount(1);
    }

    [Fact]
    public async Task CodeSignIn_With_Correct_Code_Should_Issue_Session_And_Consume_Code()
    {
        var account = GivenAccount();
        await RequestHandler().Handle(new RequestPortalCodeCommand(account.Email, OwnerPortalCodePurpose.SignIn), CancellationToken.None);

        var result = await CodeHandler().Handle(new PortalCodeSignInCommand(account.Email, "123456"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("access");
        _f.AddedRefreshTokens.Should().ContainSingle(t => t.OwnerAccountId == account.Id && t.TokenHash == "h:raw-token");
        _f.AddedCodes.Single().ConsumedAtUtc.Should().NotBeNull();
        account.LastLoginAtUtc.Should().Be(OwnerPortalTestFixture.Now);

        var reuse = await CodeHandler().Handle(new PortalCodeSignInCommand(account.Email, "123456"), CancellationToken.None);
        reuse.Error.Code.Should().Be(InvalidCredentials, "kod tek kullanımlıktır");
    }

    [Fact]
    public async Task CodeSignIn_With_Wrong_Code_Should_Count_Attempt_And_Save()
    {
        var account = GivenAccount();
        await RequestHandler().Handle(new RequestPortalCodeCommand(account.Email, OwnerPortalCodePurpose.SignIn), CancellationToken.None);

        var result = await CodeHandler().Handle(new PortalCodeSignInCommand(account.Email, "000000"), CancellationToken.None);

        result.Error.Code.Should().Be(InvalidCredentials);
        account.FailedSignInCount.Should().Be(1);
        _f.AddedCodes.Single().FailedAttempts.Should().Be(1);
        _f.Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CodeSignIn_For_Unknown_Email_Should_Return_Same_Error()
    {
        var result = await CodeHandler().Handle(new PortalCodeSignInCommand("nobody@example.com", "123456"), CancellationToken.None);

        result.Error.Code.Should().Be(InvalidCredentials);
    }

    [Fact]
    public async Task CodeSignIn_On_Locked_Account_Should_Reject_Even_Correct_Code()
    {
        var account = GivenAccount();
        await RequestHandler().Handle(new RequestPortalCodeCommand(account.Email, OwnerPortalCodePurpose.SignIn), CancellationToken.None);
        for (var i = 0; i < OwnerAccount.MaxFailedSignInAttempts; i++)
            account.RecordFailedSignIn(OwnerPortalTestFixture.Now);

        var result = await CodeHandler().Handle(new PortalCodeSignInCommand(account.Email, "123456"), CancellationToken.None);

        result.Error.Code.Should().Be(InvalidCredentials);
        _f.AddedCodes.Single().ConsumedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task PasswordSignIn_Should_Issue_Session_For_Correct_Password()
    {
        GivenAccount(password: "Abc12345!");

        var result = await PasswordHandler().Handle(new PortalPasswordSignInCommand("ayse@example.com", "Abc12345!"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _f.AddedRefreshTokens.Should().ContainSingle();
    }

    [Fact]
    public async Task Five_Wrong_Passwords_Should_Lock_And_Then_Reject_Correct_Password()
    {
        var account = GivenAccount(password: "Abc12345!");

        for (var i = 0; i < OwnerAccount.MaxFailedSignInAttempts; i++)
        {
            (await PasswordHandler().Handle(new PortalPasswordSignInCommand(account.Email, "Wrong123!"), CancellationToken.None))
                .Error.Code.Should().Be(InvalidCredentials);
        }

        account.IsLockedOut(OwnerPortalTestFixture.Now).Should().BeTrue();
        var locked = await PasswordHandler().Handle(new PortalPasswordSignInCommand(account.Email, "Abc12345!"), CancellationToken.None);
        locked.Error.Code.Should().Be(InvalidCredentials);
        _f.AddedRefreshTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task PasswordSignIn_Without_Password_Or_Account_Should_Return_Same_Error()
    {
        (await PasswordHandler().Handle(new PortalPasswordSignInCommand("nobody@example.com", "Abc12345!"), CancellationToken.None))
            .Error.Code.Should().Be(InvalidCredentials);

        var codeOnly = GivenAccount();
        (await PasswordHandler().Handle(new PortalPasswordSignInCommand(codeOnly.Email, "Abc12345!"), CancellationToken.None))
            .Error.Code.Should().Be(InvalidCredentials);
        codeOnly.FailedSignInCount.Should().Be(1);
    }
}
