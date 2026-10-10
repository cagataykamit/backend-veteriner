using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Users.Commands.SetDisplayName;
using Backend.Veteriner.Domain.Users;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Users;

public sealed class SetMyDisplayNameCommandHandlerTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClientContext> _client = new();

    private SetMyDisplayNameCommandHandler CreateHandler() => new(_users.Object, _client.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_User_Context_Missing()
    {
        _client.SetupGet(c => c.UserId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new SetMyDisplayNameCommand("Ali"), CancellationToken.None);

        result.Error.Code.Should().Be("Auth.Unauthorized.UserContextMissing");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_User_Not_Found()
    {
        var id = Guid.NewGuid();
        _client.SetupGet(c => c.UserId).Returns(id);
        _users.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await CreateHandler().Handle(new SetMyDisplayNameCommand("Ali"), CancellationToken.None);

        result.Error.Code.Should().Be("Users.NotFound");
    }

    [Fact]
    public async Task Handle_Should_Set_Name_And_Save()
    {
        var user = new User("ali@example.com", "hash");
        _client.SetupGet(c => c.UserId).Returns(user.Id);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await CreateHandler().Handle(new SetMyDisplayNameCommand(" Dr. Ali "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.DisplayName.Should().Be("Dr. Ali");
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Not_Save_When_Name_Too_Long()
    {
        var user = new User("ali@example.com", "hash");
        _client.SetupGet(c => c.UserId).Returns(user.Id);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await CreateHandler().Handle(
            new SetMyDisplayNameCommand(new string('a', User.MaxDisplayNameLength + 1)), CancellationToken.None);

        result.Error.Code.Should().Be("Users.Validation");
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
