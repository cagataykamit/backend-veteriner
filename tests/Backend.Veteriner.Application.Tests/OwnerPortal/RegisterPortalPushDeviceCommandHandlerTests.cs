using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Preferences;
using Backend.Veteriner.Domain.OwnerPortal;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Backend.Veteriner.Application.Tests.OwnerPortal;

/// <summary>MOBILE-003 §14.2: aynı jetonla eşzamanlı iki kayıt benzersiz indekse takılırsa kayıt yine başarılıdır.</summary>
public sealed class RegisterPortalPushDeviceCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IReadRepository<OwnerAccount>> _accounts = new();
    private readonly Mock<IRepository<OwnerPushDevice>> _devices = new();
    private readonly OwnerAccount _account;

    public RegisterPortalPushDeviceCommandHandlerTests()
    {
        _account = OwnerAccount.Create("a@b.com", "Ayşe", Now);
        _account.SetPushEnabled(true);
        _accounts.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<OwnerAccount>>(), It.IsAny<CancellationToken>())).ReturnsAsync(_account);
        _devices.Setup(r => r.ListAsync(It.IsAny<ISpecification<OwnerPushDevice>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _devices.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateException("IX_OwnerPushDevices_Token"));
    }

    private RegisterPortalPushDeviceCommandHandler Handler() => new(_accounts.Object, _devices.Object, new FakeTimeProvider(Now));

    private RegisterPortalPushDeviceCommand Command() => new(_account.Id, OwnerPushPlatform.Android, "token-1", null);

    [Fact]
    public async Task Concurrent_Duplicate_Registration_Succeeds_When_Token_Is_Now_Stored()
    {
        _devices.Setup(r => r.AnyAsync(It.IsAny<ISpecification<OwnerPushDevice>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Save_Failure_Is_Rethrown_When_Token_Is_Not_Stored()
    {
        _devices.Setup(r => r.AnyAsync(It.IsAny<ISpecification<OwnerPushDevice>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = async () => await Handler().Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
