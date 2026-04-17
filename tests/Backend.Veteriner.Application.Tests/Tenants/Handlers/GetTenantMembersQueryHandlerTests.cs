using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Tenants.Queries.GetMembers;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

public sealed class GetTenantMembersQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IReadRepository<UserTenant>> _userTenants = new();

    private GetTenantMembersQueryHandler CreateHandler()
        => new(_tenantContext.Object, _permissions.Object, _userTenants.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PermissionDenied()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(false);
        var handler = CreateHandler();

        var result = await handler.Handle(
            new GetTenantMembersQuery(Guid.NewGuid(), new PageRequest()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ContextMissing()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null);
        var handler = CreateHandler();

        var result = await handler.Handle(
            new GetTenantMembersQuery(Guid.NewGuid(), new PageRequest()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantMismatch()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());
        var handler = CreateHandler();

        var result = await handler.Handle(
            new GetTenantMembersQuery(Guid.NewGuid(), new PageRequest()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
    }

    [Fact]
    public async Task Handle_Should_ReturnEmptyPage_When_NoMembers()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _userTenants.Setup(x => x.CountAsync(It.IsAny<TenantMembersCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _userTenants.Setup(x => x.ListAsync(It.IsAny<TenantMembersPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserTenant>());

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetTenantMembersQuery(tid, new PageRequest { Page = 1, PageSize = 20 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().BeEmpty();
        result.Value.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task Handle_Should_Clamp_PageSize()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _userTenants.Setup(x => x.CountAsync(It.IsAny<TenantMembersCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _userTenants.Setup(x => x.ListAsync(It.IsAny<TenantMembersPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserTenant>());

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetTenantMembersQuery(tid, new PageRequest { Page = 1, PageSize = 999 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PageSize.Should().Be(200);
    }

    [Fact]
    public async Task Handle_Should_Pass_Search_To_Specs()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _userTenants.Setup(x => x.CountAsync(It.Is<TenantMembersCountSpec>(s => s.SearchTermLower == "a@b.com"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _userTenants.Setup(x => x.ListAsync(It.Is<TenantMembersPagedSpec>(s => s.SearchTermLower == "a@b.com"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserTenant>());

        var handler = CreateHandler();
        await handler.Handle(
            new GetTenantMembersQuery(tid, new PageRequest { Search = "  A@B.COM  " }),
            CancellationToken.None);

        _userTenants.Verify(x => x.CountAsync(It.IsAny<TenantMembersCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Map_User_Fields()
    {
        var tid = Guid.NewGuid();
        var uid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var user = new User("x@y.com", "hash");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, uid);
        user.ConfirmEmail();

        var ut = new UserTenant(uid, tid);
        typeof(UserTenant).GetProperty(nameof(UserTenant.User))!.SetValue(ut, user);

        _userTenants.Setup(x => x.CountAsync(It.IsAny<TenantMembersCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _userTenants.Setup(x => x.ListAsync(It.IsAny<TenantMembersPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserTenant> { ut });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetTenantMembersQuery(tid, new PageRequest()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle();
        result.Value.Items[0].UserId.Should().Be(uid);
        result.Value.Items[0].Email.Should().Be("x@y.com");
        result.Value.Items[0].EmailConfirmed.Should().BeTrue();
    }
}
