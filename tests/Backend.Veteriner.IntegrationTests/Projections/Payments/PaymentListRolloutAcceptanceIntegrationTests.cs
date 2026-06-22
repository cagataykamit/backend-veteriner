using System.Text.Json;
using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.Queries.GetList;
using Backend.Veteriner.Application.Payments.ReadModels;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Projections.Payments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Projections.Payments;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Projections.Payments;

/// <summary>
/// CQRS-14G + 15L — Payment list Query DB rollout acceptance / smoke.
/// Uçtan uca rollout zincirini tek yerde bağlar: production-safe default flag posture,
/// flag false → Command DB source of truth, backfill + parity + flag true → Query DB source of truth,
/// Query DB boş + flag true → fallback yok, search Query route (15L single clinic), read-model health gate ve rollback.
///
/// Bu sınıf production davranışını DEĞİŞTİRMEZ; yalnızca 14E/14F altyapısının rollout güvenliğini doğrular.
/// Per-flag/per-bileşen detayları (14E routing, 14F backfill/parity/health) ayrı sınıflarda kalır; burada
/// yalnızca rollout zincirinin acceptance seviyesinde uçtan uca davranışı doğrulanır.
/// </summary>
[Collection("payment-projection")]
public sealed class PaymentListRolloutAcceptanceIntegrationTests
{
    private readonly PaymentProjectionWebApplicationFactory _factory;

    public PaymentListRolloutAcceptanceIntegrationTests(PaymentProjectionWebApplicationFactory factory)
        => _factory = factory;

    // -------------------------------------------------------------------------
    // (a) Default flags false — rollout default posture (production-safe).
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Production.json")]
    [InlineData("appsettings.Staging.json")]
    [InlineData("appsettings.Development.json")]
    [InlineData("appsettings.IntegrationTests.json")]
    [InlineData("appsettings.LoadTest.json")]
    public void RolloutDefaults_Should_KeepProjectionAndListReadAndDashboardFinanceDisabled(string fileName)
    {
        using var document = LoadAppSettings(fileName);
        var root = document.RootElement;

        root.GetProperty("PaymentProjection").GetProperty("Enabled").GetBoolean()
            .Should().BeFalse($"{fileName} PaymentProjection:Enabled default false olmalı");

        var queryReadModels = root.GetProperty("QueryReadModels");
        queryReadModels.GetProperty("PaymentsListReadEnabled").GetBoolean()
            .Should().BeFalse($"{fileName} QueryReadModels:PaymentsListReadEnabled default false olmalı");
        queryReadModels.GetProperty("DashboardFinanceReadEnabled").GetBoolean()
            .Should().BeFalse($"{fileName} QueryReadModels:DashboardFinanceReadEnabled default false olmalı");
        queryReadModels.GetProperty("DashboardRecentPaymentsReadEnabled").GetBoolean()
            .Should().BeFalse($"{fileName} QueryReadModels:DashboardRecentPaymentsReadEnabled default false olmalı");
        queryReadModels.GetProperty("ClientPaymentSummaryReadEnabled").GetBoolean()
            .Should().BeFalse($"{fileName} QueryReadModels:ClientPaymentSummaryReadEnabled default false olmalı");
        queryReadModels.GetProperty("PaymentsReportReadEnabled").GetBoolean()
            .Should().BeFalse($"{fileName} QueryReadModels:PaymentsReportReadEnabled default false olmalı");
    }

    // -------------------------------------------------------------------------
    // (b) Flag false → Command DB source of truth (Query DB boş olsa bile).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FlagFalse_Should_UseCommandDbSourceOfTruth_EvenWhenReadModelsEmpty()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);
        var paymentId = await SeedPaymentAsync(tenantId, clinicId, "Ada Lovelace", "Pamuk", 120m, paidAt);

        // Backfill çalıştırılmadı → Query DB PaymentReadModels boş.
        await AssertReadModelEmptyAsync(tenantId);

        var result = await InvokeListAsync(tenantId, clinicId, paymentsListReadEnabled: false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(1);
        result.Value.Items.Should().ContainSingle(x => x.Id == paymentId);
    }

    // -------------------------------------------------------------------------
    // (c) Backfill + parity + flag true + search boş → Query DB source of truth,
    //     dönen alanlar Command DB davranışıyla parity.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BackfillThenFlagTrue_EmptySearch_Should_ReturnQueryDb_WithParityToCommandDb()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var older = new DateTime(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2026, 6, 20, 15, 0, 0, DateTimeKind.Utc);
        await SeedCustomPaymentAsync(tenantId, clinicId, "  Ada Lovelace  ", "  Lord Byron  ", 275.50m, "TRY", PaymentMethod.Card, older, "  Yıllık aşı  ");
        await SeedCustomPaymentAsync(tenantId, clinicId, "Grace Hopper", null, 90m, "TRY", PaymentMethod.Cash, newer, null);

        var backfill = await RunBackfillAsync(tenantId);
        backfill.Success.Should().BeTrue();
        backfill.ParityInSync.Should().BeTrue();

        var parity = await GetParityAsync(tenantId, clinicId);
        parity.InSync.Should().BeTrue("rollout flag açmadan önce parity InSync olmalı");

        var command = await InvokeListAsync(tenantId, clinicId, paymentsListReadEnabled: false);
        var query = await InvokeListAsync(tenantId, clinicId, paymentsListReadEnabled: true);

        command.IsSuccess.Should().BeTrue();
        query.IsSuccess.Should().BeTrue();
        query.Value!.TotalItems.Should().Be(command.Value!.TotalItems);
        query.Value.Items.Should().HaveCount(command.Value.Items.Count);

        // amount, currency, method, paidAt, client/pet names, pagination — Command DB davranışı ile uyumlu.
        query.Value.Items.Should().BeEquivalentTo(
            command.Value.Items,
            options => options.WithStrictOrdering(),
            "Query DB list öğeleri Command DB ile aynı sıra ve alan değerlerinde olmalı (PaidAtUtc DESC, Id DESC)");
    }

    // -------------------------------------------------------------------------
    // (d) Query DB boş + flag true + search boş → fallback yok (boş paged result).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task QueryDbEmpty_FlagTrue_EmptySearch_Should_ReturnEmpty_WithoutCommandFallback()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 21, 10, 0, 0, DateTimeKind.Utc);
        var paymentId = await SeedPaymentAsync(tenantId, clinicId, "Ada Lovelace", "Pamuk", 200m, paidAt);

        // Backfill yapılmadı → Query DB boş. Flag true route Query DB'ye gider.
        await AssertReadModelEmptyAsync(tenantId);

        var query = await InvokeListAsync(tenantId, clinicId, paymentsListReadEnabled: true);
        query.IsSuccess.Should().BeTrue();
        query.Value!.TotalItems.Should().Be(0, "Query path seçildiğinde Command DB'ye fallback yapılmaz");
        query.Value.Items.Should().BeEmpty();

        // Command DB'de kayıt mevcut: flag false ile görünür (source of truth orada).
        var command = await InvokeListAsync(tenantId, clinicId, paymentsListReadEnabled: false);
        command.Value!.TotalItems.Should().Be(1);
        command.Value.Items.Should().ContainSingle(x => x.Id == paymentId);
    }

    // -------------------------------------------------------------------------
    // (e) CQRS-15L: Search dolu + flag true + single clinic → Query DB search path;
    //     Query DB boş → fallback yok. Multi-clinic scope → Command DB fallback (search guard).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SearchProvided_FlagTrue_SingleClinic_Should_UseQueryDbWithoutCommandFallback_WhenQueryDbEmpty()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 22, 10, 0, 0, DateTimeKind.Utc);
        await SeedCustomPaymentAsync(
            tenantId, clinicId, "Ayşe Yılmaz", "Pamuk", 150m, "TRY", PaymentMethod.Cash, paidAt, "Nakit tahsilat");

        // Query DB boş; single clinic + search dolu → Query path (15L). Command DB fallback yok.
        await AssertReadModelEmptyAsync(tenantId);

        var result = await InvokeListAsync(tenantId, clinicId, paymentsListReadEnabled: true, search: "tahsilat");

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(0, "Query path seçildiğinde Command DB'ye fallback yapılmaz");
        result.Value.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchProvided_FlagTrue_MultiClinicScope_Should_UseCommandDbFallback()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 22, 11, 0, 0, DateTimeKind.Utc);
        var paymentId = await SeedCustomPaymentAsync(
            tenantId, clinicId, "Ayşe Yılmaz", "Pamuk", 150m, "TRY", PaymentMethod.Cash, paidAt, "Nakit tahsilat");

        await AssertReadModelEmptyAsync(tenantId);

        var result = await InvokeListWithMultiClinicScopeAsync(
            tenantId, clinicId, paymentsListReadEnabled: true, search: "tahsilat");

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(1, "multi-clinic scope represent edilemediğinden Command DB path kullanılır");
        result.Value.Items.Should().ContainSingle(x => x.Id == paymentId);
    }

    // -------------------------------------------------------------------------
    // (f) Health gate — read-model drift sinyalinin rollout boyunca davranışı.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HealthGate_ProjectionAndListReadDisabled_Should_StayHealthy_EvenWithEmptyReadModel()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        await SeedPaymentAsync(tenantId, clinicId, "Ada Lovelace", null, 100m, DateTime.UtcNow);

        // Backfill yapılmadı → drift var; ama projection ve list read kapalı → gate kapalı → sistem bozulmaz.
        var signal = await GetReadModelSignalAsync(paymentsListReadEnabled: false);
        signal.CountInSync.Should().BeFalse();

        var evaluation = Evaluate(projectionEnabled: false, signal);
        evaluation.Level.Should().Be(PaymentProjectionHealthLevel.Healthy);
    }

    [Fact]
    public async Task HealthGate_Drift_WithListReadEnabled_Should_BeUnhealthy()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        await SeedPaymentAsync(tenantId, clinicId, "Ada Lovelace", null, 100m, DateTime.UtcNow);
        await SeedPaymentAsync(tenantId, clinicId, "Grace Hopper", null, 200m, DateTime.UtcNow);

        var signal = await GetReadModelSignalAsync(paymentsListReadEnabled: true);
        signal.CountInSync.Should().BeFalse();

        var evaluation = Evaluate(projectionEnabled: true, signal);
        evaluation.Level.Should().Be(PaymentProjectionHealthLevel.Unhealthy);
    }

    [Fact]
    public async Task HealthGate_Drift_WithOnlyProjectionEnabled_Should_BeDegraded()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        await SeedPaymentAsync(tenantId, clinicId, "Ada Lovelace", null, 100m, DateTime.UtcNow);

        var signal = await GetReadModelSignalAsync(paymentsListReadEnabled: false);
        signal.CountInSync.Should().BeFalse();

        var evaluation = Evaluate(projectionEnabled: true, signal);
        evaluation.Level.Should().Be(PaymentProjectionHealthLevel.Degraded);
    }

    [Fact]
    public async Task HealthGate_AfterBackfill_ParityInSync_Should_BeHealthy()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        await SeedPaymentAsync(tenantId, clinicId, "Ada Lovelace", "Pamuk", 100m, DateTime.UtcNow);

        var backfill = await RunBackfillAsync(tenantId);
        backfill.ParityInSync.Should().BeTrue();

        var signal = await GetReadModelSignalAsync(paymentsListReadEnabled: true);
        signal.CountInSync.Should().BeTrue();

        var evaluation = Evaluate(projectionEnabled: true, signal);
        evaluation.Level.Should().Be(PaymentProjectionHealthLevel.Healthy);
    }

    [Fact]
    public async Task HealthGate_DeadLetter_Should_StayUnhealthy_EvenWhenReadModelInSync()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        await SeedPaymentAsync(tenantId, clinicId, "Ada Lovelace", null, 100m, DateTime.UtcNow);
        await RunBackfillAsync(tenantId);

        var signal = await GetReadModelSignalAsync(paymentsListReadEnabled: true);
        signal.CountInSync.Should().BeTrue();

        // Read-model InSync olsa bile mevcut dead-letter severity kuralı korunur.
        var status = CreateStatus(deadLetterCount: 1, projectionEnabled: true);
        var evaluation = PaymentProjectionHealthEvaluator.Evaluate(
            status,
            new PaymentProjectionHealthOptions { DeadLetterIsUnhealthy = true },
            signal);

        evaluation.Level.Should().Be(PaymentProjectionHealthLevel.Unhealthy);
    }

    // -------------------------------------------------------------------------
    // (g) Rollback — flag false production list davranışını her zaman kurtarır.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Rollback_FlagFalse_Should_RestoreCommandDb_EvenWhenQueryDbDrifted()
    {
        await ResetAsync();
        var (tenantId, clinicId) = await SeedTenantAsync();
        var paidAt = new DateTime(2026, 6, 23, 10, 0, 0, DateTimeKind.Utc);
        var paymentId = await SeedPaymentAsync(tenantId, clinicId, "Ada Lovelace", "Pamuk", 320m, paidAt);
        await RunBackfillAsync(tenantId);

        // Flag true iken Query DB route kullanılır (parity InSync).
        var beforeRollback = await InvokeListAsync(tenantId, clinicId, paymentsListReadEnabled: true);
        beforeRollback.Value!.TotalItems.Should().Be(1);

        // Query DB drift/boşluk simülasyonu: read-model satırını sil → Query path boş döner.
        await DeleteReadModelsAsync(tenantId);
        var driftedQuery = await InvokeListAsync(tenantId, clinicId, paymentsListReadEnabled: true);
        driftedQuery.Value!.TotalItems.Should().Be(0, "Query path drift sonrası fallback yapmadan boş döner");

        // Rollback: flag false → Command DB list davranışı kurtarılır (drift'ten bağımsız).
        var afterRollback = await InvokeListAsync(tenantId, clinicId, paymentsListReadEnabled: false);
        afterRollback.IsSuccess.Should().BeTrue();
        afterRollback.Value!.TotalItems.Should().Be(1, "flag false production list davranışını her zaman kurtarır");
        afterRollback.Value.Items.Should().ContainSingle(x => x.Id == paymentId);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task ResetAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        await PaymentProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PaymentProjectionTestSupport.ClearOutboxAsync(commandDb);
        await commandDb.Payments.ExecuteDeleteAsync();
    }

    private async Task<(Guid TenantId, Guid ClinicId)> SeedTenantAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (tenantId, clinicId, _) = await PaymentFinanceTestSupport.SeedTenantWithTwoClinicsAsync(commandDb);
        return (tenantId, clinicId);
    }

    private async Task<Guid> SeedPaymentAsync(
        Guid tenantId,
        Guid clinicId,
        string clientName,
        string? petName,
        decimal amount,
        DateTime paidAtUtc)
        => await SeedCustomPaymentAsync(tenantId, clinicId, clientName, petName, amount, "TRY", PaymentMethod.Cash, paidAtUtc, null);

    private async Task<Guid> SeedCustomPaymentAsync(
        Guid tenantId,
        Guid clinicId,
        string clientName,
        string? petName,
        decimal amount,
        string currency,
        PaymentMethod method,
        DateTime paidAtUtc,
        string? notes)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var client = new Client(tenantId, clientName);
        commandDb.Clients.Add(client);
        await commandDb.SaveChangesAsync();

        Guid? petId = null;
        if (petName is not null)
        {
            var speciesId = await commandDb.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(tenantId, client.Id, petName, speciesId);
            commandDb.Pets.Add(pet);
            await commandDb.SaveChangesAsync();
            petId = pet.Id;
        }

        var payment = new Payment(
            tenantId, clinicId, client.Id, petId,
            appointmentId: null, examinationId: null,
            amount, currency, method, paidAtUtc, notes);
        commandDb.Payments.Add(payment);
        await commandDb.SaveChangesAsync();
        return payment.Id;
    }

    private async Task<PaymentReadModelBackfillResult> RunBackfillAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var backfill = scope.ServiceProvider.GetRequiredService<IPaymentReadModelBackfillService>();
        return await backfill.BackfillAsync(tenantId, batchSize: 100, CancellationToken.None);
    }

    private async Task<PaymentReadModelParityResult> GetParityAsync(Guid tenantId, Guid clinicId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var parity = scope.ServiceProvider.GetRequiredService<IPaymentReadModelParityReader>();
        return await parity.GetClinicParityAsync(tenantId, clinicId, cancellationToken: CancellationToken.None);
    }

    private async Task<PaymentReadModelHealthSignal> GetReadModelSignalAsync(bool paymentsListReadEnabled)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IPaymentReadModelHealthReader>();
        var real = await reader.GetSignalAsync(CancellationToken.None);
        // Gerçek Command/Query sayımları korunur; yalnızca flag boyutu acceptance senaryosuna göre projekte edilir.
        return new PaymentReadModelHealthSignal(real.CommandPaymentCount, real.ReadModelCount, paymentsListReadEnabled);
    }

    private async Task AssertReadModelEmptyAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        (await queryDb.PaymentReadModels.CountAsync(x => x.TenantId == tenantId)).Should().Be(0);
    }

    private async Task DeleteReadModelsAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        await queryDb.PaymentReadModels.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync();
    }

    private async Task<Result<PagedResult<PaymentListItemDto>>> InvokeListAsync(
        Guid tenantId,
        Guid clinicId,
        bool paymentsListReadEnabled,
        string? search = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var handler = new GetPaymentsListQueryHandler(
            new FixedTenantContext(tenantId),
            new FixedClinicContext(clinicId),
            new PassthroughClinicReadScopeResolver(),
            sp.GetRequiredService<IReadRepository<Payment>>(),
            sp.GetRequiredService<IReadRepository<Pet>>(),
            sp.GetRequiredService<IReadRepository<Client>>(),
            sp.GetRequiredService<IClientReadModelLookupReader>(),
            sp.GetRequiredService<IPetReadModelLookupReader>(),
            sp.GetRequiredService<IPaymentsListReadModelReader>(),
            Options.Create(new QueryReadModelsOptions
            {
                PaymentsListReadEnabled = paymentsListReadEnabled
            }));

        return await handler.Handle(
            new GetPaymentsListQuery(
                new PaymentListPagingRequest { Page = 1, PageSize = 50 },
                clinicId,
                Search: search),
            CancellationToken.None);
    }

    private async Task<Result<PagedResult<PaymentListItemDto>>> InvokeListWithMultiClinicScopeAsync(
        Guid tenantId,
        Guid clinicId,
        bool paymentsListReadEnabled,
        string? search = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var (_, _, clinicB) = await GetTwoClinicIdsAsync(tenantId);
        var accessibleClinicIds = new[] { clinicId, clinicB };

        var handler = new GetPaymentsListQueryHandler(
            new FixedTenantContext(tenantId),
            new FixedClinicContext(clinicId),
            new MultiClinicReadScopeResolver(accessibleClinicIds),
            sp.GetRequiredService<IReadRepository<Payment>>(),
            sp.GetRequiredService<IReadRepository<Pet>>(),
            sp.GetRequiredService<IReadRepository<Client>>(),
            sp.GetRequiredService<IClientReadModelLookupReader>(),
            sp.GetRequiredService<IPetReadModelLookupReader>(),
            sp.GetRequiredService<IPaymentsListReadModelReader>(),
            Options.Create(new QueryReadModelsOptions
            {
                PaymentsListReadEnabled = paymentsListReadEnabled
            }));

        return await handler.Handle(
            new GetPaymentsListQuery(
                new PaymentListPagingRequest { Page = 1, PageSize = 50 },
                clinicId,
                Search: search),
            CancellationToken.None);
    }

    private async Task<(Guid TenantId, Guid ClinicA, Guid ClinicB)> GetTwoClinicIdsAsync(Guid tenantId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clinics = await commandDb.Clinics
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .Take(2)
            .ToListAsync();
        clinics.Should().HaveCount(2);
        return (tenantId, clinics[0], clinics[1]);
    }

    private static PaymentProjectionHealthEvaluation Evaluate(bool projectionEnabled, PaymentReadModelHealthSignal signal)
        => PaymentProjectionHealthEvaluator.Evaluate(
            CreateStatus(projectionEnabled: projectionEnabled),
            new PaymentProjectionHealthOptions { DegradedAfterSeconds = 10, UnhealthyAfterSeconds = 30, DeadLetterIsUnhealthy = true },
            signal);

    private static PaymentProjectionStatus CreateStatus(
        int pendingCount = 0,
        int retryWaitingCount = 0,
        int deadLetterCount = 0,
        bool projectionEnabled = false)
        => new(
            pendingCount,
            retryWaitingCount,
            deadLetterCount,
            OldestPendingCreatedAtUtc: null,
            OldestPendingAge: null,
            NextRetryAtUtc: null,
            QueryDatabaseReachable: true,
            QueryDatabaseHasPendingMigrations: false,
            ProjectionEnabled: projectionEnabled);

    private static JsonDocument LoadAppSettings(string fileName)
    {
        var path = Path.Combine(ResolveApiProjectDirectory(), fileName);
        File.Exists(path).Should().BeTrue($"appsettings bulunamadı: {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string ResolveApiProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Backend.Veteriner.Api");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Backend.Veteriner.Api dizini bulunamadı.");
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
    }

    private sealed class FixedClinicContext(Guid clinicId) : IClinicContext
    {
        public Guid? ClinicId { get; } = clinicId;
    }

    private sealed class PassthroughClinicReadScopeResolver : IClinicReadScopeResolver
    {
        public Task<Result<ClinicReadScope>> ResolveAsync(Guid tenantId, Guid? requestClinicId, CancellationToken ct)
            => Task.FromResult(Result<ClinicReadScope>.Success(new ClinicReadScope(requestClinicId, null)));
    }

    /// <summary>ClinicAdmin multi-clinic: SingleClinicId null + AccessibleClinicIds dolu → Query path temsil edilemez.</summary>
    private sealed class MultiClinicReadScopeResolver(IReadOnlyCollection<Guid> accessibleClinicIds) : IClinicReadScopeResolver
    {
        public Task<Result<ClinicReadScope>> ResolveAsync(Guid tenantId, Guid? requestClinicId, CancellationToken ct)
            => Task.FromResult(Result<ClinicReadScope>.Success(new ClinicReadScope(null, accessibleClinicIds)));
    }
}
