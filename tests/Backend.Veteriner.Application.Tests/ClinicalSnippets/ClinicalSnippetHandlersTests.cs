using Backend.Veteriner.Application.ClinicalSnippets.Commands.Create;
using Backend.Veteriner.Application.ClinicalSnippets.Commands.SetActive;
using Backend.Veteriner.Application.ClinicalSnippets.Commands.Update;
using Backend.Veteriner.Application.ClinicalSnippets.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Catalog;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.ClinicalSnippets;

public sealed class ClinicalSnippetHandlersTests
{
    private static readonly string ValidRowVersion = Convert.ToBase64String(new byte[] { 0, 0, 0, 0, 0, 0, 0, 7 });

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IRepository<ClinicalSnippet>> _snippets = new();

    public ClinicalSnippetHandlersTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _snippets.Setup(r => r.AddAsync(It.IsAny<ClinicalSnippet>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicalSnippet s, CancellationToken _) => s);
        _snippets.Setup(r => r.UpdateAsync(It.IsAny<ClinicalSnippet>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _snippets.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        ShortcutTaken(false);
        TenantCount(0);
    }

    private void ShortcutTaken(bool taken)
        => _snippets.Setup(r => r.AnyAsync(It.IsAny<ClinicalSnippetShortcutTakenSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(taken);

    private void TenantCount(int count)
        => _snippets.Setup(r => r.CountAsync(It.IsAny<ClinicalSnippetsByTenantCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(count);

    private ClinicalSnippet Existing()
    {
        var snippet = ClinicalSnippet.Create(_tenantId, "kusma", "Kusma", "Kaç kez?", DateTime.UtcNow).Value!;
        _snippets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicalSnippetForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snippet);
        _snippets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicalSnippetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snippet);
        return snippet;
    }

    private CreateClinicalSnippetCommandHandler CreateHandler() => new(_tenantContext.Object, _snippets.Object, TimeProvider.System);
    private UpdateClinicalSnippetCommandHandler UpdateHandler() => new(_tenantContext.Object, _snippets.Object, TimeProvider.System);
    private SetClinicalSnippetActiveCommandHandler SetActiveHandler() => new(_tenantContext.Object, _snippets.Object, TimeProvider.System);

    [Fact]
    public async Task Create_Should_Add_Tenant_Snippet_With_Normalized_Shortcut()
    {
        ClinicalSnippet? added = null;
        _snippets.Setup(r => r.AddAsync(It.IsAny<ClinicalSnippet>(), It.IsAny<CancellationToken>()))
            .Callback<ClinicalSnippet, CancellationToken>((s, _) => added = s)
            .ReturnsAsync((ClinicalSnippet s, CancellationToken _) => s);

        var result = await CreateHandler().Handle(
            new CreateClinicalSnippetCommand("#İshal", "İshal anamnezi", "Kaç gündür?"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Shortcut.Should().Be("ishal");
        added!.TenantId.Should().Be(_tenantId);
    }

    [Fact]
    public async Task Create_Should_Return_Duplicate_When_Shortcut_Taken()
    {
        ShortcutTaken(true);

        var result = await CreateHandler().Handle(
            new CreateClinicalSnippetCommand("kusma", "Kusma", "Metin"), CancellationToken.None);

        result.Error.Code.Should().Be("ClinicalSnippets.DuplicateShortcut");
        _snippets.Verify(r => r.AddAsync(It.IsAny<ClinicalSnippet>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_Map_Unique_Index_Race_To_Duplicate()
    {
        _snippets.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("save failed", new Exception(
                "Cannot insert duplicate key row with unique index 'IX_ClinicalSnippets_TenantId_Shortcut'.")));

        var result = await CreateHandler().Handle(
            new CreateClinicalSnippetCommand("kusma", "Kusma", "Metin"), CancellationToken.None);

        result.Error.Code.Should().Be("ClinicalSnippets.DuplicateShortcut");
    }

    [Fact]
    public async Task Create_Should_Stop_At_Tenant_Limit()
    {
        TenantCount(ClinicalSnippet.MaxPerTenant);

        var result = await CreateHandler().Handle(
            new CreateClinicalSnippetCommand("kusma", "Kusma", "Metin"), CancellationToken.None);

        result.Error.Code.Should().Be("ClinicalSnippets.LimitReached");
    }

    [Fact]
    public async Task Create_Should_Return_Validation_For_Invalid_Shortcut()
    {
        var result = await CreateHandler().Handle(
            new CreateClinicalSnippetCommand("iki kelime", "Başlık", "Metin"), CancellationToken.None);

        result.Error.Code.Should().Be("ClinicalSnippets.Validation");
    }

    [Fact]
    public async Task Update_Should_Require_RowVersion_And_Map_Concurrency()
    {
        Existing();

        var missing = await UpdateHandler().Handle(
            new UpdateClinicalSnippetCommand(Guid.NewGuid(), "kusma", "Kusma", "Metin", null), CancellationToken.None);
        missing.Error.Code.Should().Be("ClinicalSnippets.Validation");

        _snippets.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateConcurrencyException());
        var conflict = await UpdateHandler().Handle(
            new UpdateClinicalSnippetCommand(Guid.NewGuid(), "kusma", "Kusma", "Yeni", ValidRowVersion), CancellationToken.None);
        conflict.Error.Code.Should().Be("ClinicalSnippets.ConcurrencyConflict");
    }

    [Fact]
    public async Task Update_Should_Change_Text_And_Check_Duplicate_Excluding_Self()
    {
        var snippet = Existing();

        var result = await UpdateHandler().Handle(
            new UpdateClinicalSnippetCommand(snippet.Id, "#kusma-2", "Kusma 2", "Yeni metin", ValidRowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Shortcut.Should().Be("kusma-2");
        result.Value.Body.Should().Be("Yeni metin");
    }

    [Fact]
    public async Task Update_Should_Return_NotFound_For_Other_Tenant()
    {
        var result = await UpdateHandler().Handle(
            new UpdateClinicalSnippetCommand(Guid.NewGuid(), "kusma", "Kusma", "Metin", ValidRowVersion), CancellationToken.None);

        result.Error.Code.Should().Be("ClinicalSnippets.NotFound");
    }

    [Fact]
    public async Task SetActive_Should_Deactivate_And_Be_Idempotent()
    {
        var snippet = Existing();

        (await SetActiveHandler().Handle(new SetClinicalSnippetActiveCommand(snippet.Id, false), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        (await SetActiveHandler().Handle(new SetClinicalSnippetActiveCommand(snippet.Id, false), CancellationToken.None))
            .IsSuccess.Should().BeTrue();

        snippet.IsActive.Should().BeFalse();
    }
}
