using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Queries.GetList;
using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Domain.Clients;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clients.Handlers;

public sealed class GetClientsListQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IClientReadModelReader> _readModelReader = new();

    private GetClientsListQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clients.Object,
            _readModelReader.Object,
            Options.Create(new QueryReadModelsOptions { ClientsEnabled = false }));

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var query = new GetClientsListQuery(new PageRequest { Page = 1, PageSize = 20 });

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _clients.Verify(
            r => r.CountAsync(It.IsAny<ClientsByTenantCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnEmptyPage_When_NoRows()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clients.Setup(r => r.CountAsync(It.IsAny<ClientsByTenantCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientListItemDto>());

        var query = new GetClientsListQuery(new PageRequest { Page = 1, PageSize = 20 });
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var p = result.Value!;
        p.Items.Should().BeEmpty();
        p.TotalItems.Should().Be(0);
        p.Page.Should().Be(1);
        p.PageSize.Should().Be(20);
        p.TotalPages.Should().Be(0);
    }

    [Fact]
    public async Task Handle_Should_MapListItems_When_ClientsExist()
    {
        var tid = Guid.NewGuid();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var created = DateTime.UtcNow;
        var rows = new List<ClientListItemDto>
        {
            new(id1, tid, created, "B", null, null),
            new(id2, tid, created, "A", "a@b.com", "05321111111")
        };

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clients.Setup(r => r.CountAsync(It.IsAny<ClientsByTenantCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        var query = new GetClientsListQuery(new PageRequest { Page = 1, PageSize = 20 });
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var p = result.Value!;
        p.Items.Should().HaveCount(2);
        p.TotalItems.Should().Be(2);
        p.Items[0].FullName.Should().Be("B");
        p.Items[1].FullName.Should().Be("A");
        p.Items[1].Email.Should().Be("a@b.com");
    }

    [Fact]
    public async Task Handle_Should_ClampPageAndPageSize()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clients.Setup(r => r.CountAsync(It.IsAny<ClientsByTenantCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientListItemDto>());

        var query = new GetClientsListQuery(new PageRequest { Page = 0, PageSize = 500 });
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(200);
    }

    [Fact]
    public async Task Handle_Should_CallCountAndList_When_SearchWhitespaceOnly()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clients.Setup(r => r.CountAsync(It.IsAny<ClientsByTenantCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientListItemDto>());

        var query = new GetClientsListQuery(new PageRequest { Page = 1, PageSize = 20, Search = "   " });
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _clients.Verify(
            r => r.CountAsync(It.IsAny<ClientsByTenantCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantPagedSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
