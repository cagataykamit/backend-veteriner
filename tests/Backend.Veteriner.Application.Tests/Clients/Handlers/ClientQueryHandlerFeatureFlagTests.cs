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

public sealed class ClientQueryHandlerFeatureFlagTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IClientReadModelReader> _readModelReader = new();

    [Fact]
    public async Task List_WhenFlagFalse_Should_UseCommandRepository_NotQueryReader()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clients.Setup(r => r.CountAsync(It.IsAny<ClientsByTenantCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientListItemDto>());

        var handler = CreateListHandler(false);
        await handler.Handle(new GetClientsListQuery(new PageRequest()), CancellationToken.None);

        _clients.Verify(r => r.CountAsync(It.IsAny<ClientsByTenantCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _clients.Verify(r => r.ListAsync(It.IsAny<ClientsByTenantPagedSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _readModelReader.Verify(r => r.GetListAsync(It.IsAny<ClientListReadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_WhenFlagTrue_Should_UseQueryReader_NotCommandRepository()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _readModelReader.Setup(r => r.GetListAsync(It.IsAny<ClientListReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientListReadResult(Array.Empty<ClientListItemDto>(), 0));

        var handler = CreateListHandler(true);
        await handler.Handle(new GetClientsListQuery(new PageRequest()), CancellationToken.None);

        _readModelReader.Verify(r => r.GetListAsync(It.IsAny<ClientListReadRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _clients.Verify(r => r.CountAsync(It.IsAny<ClientsByTenantCountSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _clients.Verify(r => r.ListAsync(It.IsAny<ClientsByTenantPagedSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_WhenFlagTrue_Should_PassTenantScopeAndSearchToReader()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        ClientListReadRequest? captured = null;
        _readModelReader.Setup(r => r.GetListAsync(It.IsAny<ClientListReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ClientListReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new ClientListReadResult(Array.Empty<ClientListItemDto>(), 0));

        var handler = CreateListHandler(true);
        await handler.Handle(
            new GetClientsListQuery(new PageRequest { Page = 2, PageSize = 25, Search = "ayşe" }),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(tid);
        captured.Page.Should().Be(2);
        captured.PageSize.Should().Be(25);
        captured.SearchContainsLikePattern.Should().Be("%ayşe%");
    }

    [Fact]
    public async Task List_WhenFlagTrue_AndTenantMissing_Should_ReturnFailure_WithoutHittingReader()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var handler = CreateListHandler(true);
        var result = await handler.Handle(new GetClientsListQuery(new PageRequest()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _readModelReader.Verify(r => r.GetListAsync(It.IsAny<ClientListReadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_WhenQueryReaderThrows_Should_NotFallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _readModelReader.Setup(r => r.GetListAsync(It.IsAny<ClientListReadRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("query db unavailable"));

        var handler = CreateListHandler(true);
        var act = () => handler.Handle(new GetClientsListQuery(new PageRequest()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _clients.Verify(r => r.CountAsync(It.IsAny<ClientsByTenantCountSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private GetClientsListQueryHandler CreateListHandler(bool enabled)
        => new(
            _tenantContext.Object,
            _clients.Object,
            _readModelReader.Object,
            Options.Create(new QueryReadModelsOptions { ClientsEnabled = enabled }));
}
