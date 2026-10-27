using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Application.SpeciesReference.Commands.Create;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Infrastructure.Behaviors;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// ADR-039: repository yazımları kaydetmez; handler'daki tek SaveChanges komutun yazımlarını tek commit'te toplar.
/// Başarısız kayıt hiçbir şey bırakmaz ve izleyiciyi boşaltır; kayıt ağı unutulmuş kaydı yakalar.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class UnitOfWorkIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public UnitOfWorkIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Repository_Add_Should_Not_Persist_Until_SaveChanges()
    {
        var species = NewSpecies();
        try
        {
            await using var scope = _factory.Services.CreateSystemScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Species>>();

            await repository.AddAsync(species);
            (await ExistsAsync(species.Id)).Should().BeFalse();

            await repository.SaveChangesAsync();
            (await ExistsAsync(species.Id)).Should().BeTrue();
        }
        finally
        {
            await DeleteAsync(species.Id);
        }
    }

    [Fact]
    public async Task Failed_SaveChanges_Should_Persist_No_Write_And_Clear_Tracker()
    {
        var first = NewSpecies();
        var duplicate = new Species(first.Code, "Aynı kod");
        try
        {
            await using var scope = _factory.Services.CreateSystemScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Species>>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            // Eski davranışta ilk AddAsync kendi commit'ini yapardı; ikinci yazım düşünce ilk satır kalırdı.
            await repository.AddAsync(first);
            await repository.AddAsync(duplicate);

            var save = async () => await uow.SaveChangesAsync();
            await save.Should().ThrowAsync<DbUpdateException>();

            (await ExistsAsync(first.Id)).Should().BeFalse();
            (await ExistsAsync(duplicate.Id)).Should().BeFalse();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().ChangeTracker.Entries().Should().BeEmpty();
        }
        finally
        {
            await DeleteAsync(first.Id);
            await DeleteAsync(duplicate.Id);
        }
    }

    [Fact]
    public async Task Guard_Should_Throw_When_Successful_Request_Leaves_Unsaved_Changes()
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var guard = CreateGuard(scope.ServiceProvider);

        var handle = async () => await guard.Handle(
            NewCommand(),
            (_, _) =>
            {
                db.Species.Add(NewSpecies());
                return ValueTask.FromResult(Result<Guid>.Success(Guid.NewGuid()));
            },
            CancellationToken.None);

        await handle.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ADR-039*");
    }

    [Fact]
    public async Task Guard_Should_Throw_When_Outbox_Buffer_Is_Not_Flushed()
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var buffer = scope.ServiceProvider.GetRequiredService<IOutboxBuffer>();
        var guard = CreateGuard(scope.ServiceProvider);

        var handle = async () => await guard.Handle(
            NewCommand(),
            async (_, ct) =>
            {
                await buffer.EnqueueAsync("Test", "{}", ct);
                return Result<Guid>.Success(Guid.NewGuid());
            },
            CancellationToken.None);

        await handle.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Guard_Should_Allow_Failed_Result_And_Nested_Request_With_Pending_Changes()
    {
        await using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outer = CreateGuard(scope.ServiceProvider);
        var inner = CreateGuard(scope.ServiceProvider);

        // Başarısız sonuç: bekleyen değişiklik atılır, istisna yok.
        var failed = await outer.Handle(
            NewCommand(),
            (_, _) =>
            {
                db.Species.Add(NewSpecies());
                return ValueTask.FromResult(Result<Guid>.Failure("Test.Failed", "başarısız"));
            },
            CancellationToken.None);
        failed.IsSuccess.Should().BeFalse();
        db.ChangeTracker.Clear();

        // İç istek dıştaki handler'ın henüz kaydetmediği değişiklikle başarılı dönebilir; dış istek sonra temizler.
        var result = await outer.Handle(
            NewCommand(),
            async (_, ct) =>
            {
                var added = db.Species.Add(NewSpecies());
                var nested = await inner.Handle(
                    NewCommand(), (_, _) => ValueTask.FromResult(Result<Guid>.Success(Guid.NewGuid())), ct);
                added.State = EntityState.Detached;
                return nested;
            },
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    private static UnsavedChangesGuardBehavior<CreateSpeciesCommand, Result<Guid>> CreateGuard(IServiceProvider services)
        => new(services.GetRequiredService<AppDbContext>(), services.GetRequiredService<OutboxBuffer>());

    private static CreateSpeciesCommand NewCommand() => new("UOW", "Birim");

    private static Species NewSpecies() => new($"UOW-{Guid.NewGuid():N}"[..20], "Birim iş testi");

    private async Task<bool> ExistsAsync(Guid id)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Species.AnyAsync(s => s.Id == id);
    }

    private async Task DeleteAsync(Guid id)
    {
        await using var scope = _factory.Services.CreateSystemScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Species.Where(s => s.Id == id).ExecuteDeleteAsync();
    }
}
