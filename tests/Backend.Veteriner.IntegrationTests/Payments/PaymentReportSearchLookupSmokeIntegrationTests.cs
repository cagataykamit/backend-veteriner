using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Payments.Queries.GetPaymentReport;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Clients;
using Backend.IntegrationTests.Projections.Pets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Payments;

/// <summary>
/// CQRS-12D-8 smoke: payment report Strategy B shared search lookup (report only).
/// </summary>
[Collection("pet-projection")]
public sealed class PaymentReportSearchLookupSmokeIntegrationTests
{
    private readonly PetProjectionWebApplicationFactory _factory;

    public PaymentReportSearchLookupSmokeIntegrationTests(PetProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task FlagOff_Should_FindReport_ByClientNameSearch_EvenWhenQueryDbEmpty()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQuerySideAsync(queryDb);
        var seed = await SeedPaymentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk", amount: 120m);

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seed.TenantId,
            seed.ClinicId,
            search: "yılmaz",
            paymentsSearchLookupEnabled: false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        result.Value.TotalAmount.Should().Be(120m);
    }

    [Fact]
    public async Task FlagOn_Should_NotFallback_WhenReadModelsEmpty()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQuerySideAsync(queryDb);
        await SeedPaymentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk", amount: 120m);
        var seed = await SeedPaymentAsync(commandDb, clientFullName: "Mehmet Demir", petName: "Boncuk", amount: 80m);

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seed.TenantId,
            seed.ClinicId,
            search: "yılmaz",
            paymentsSearchLookupEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(0);
        result.Value.TotalAmount.Should().Be(0m);
    }

    [Fact]
    public async Task FlagOn_Should_FindReport_ByClientNameSearch_WhenClientReadModelPopulated()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQuerySideAsync(queryDb);
        var seed = await SeedPaymentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk", amount: 150m);
        await SeedClientReadModelAsync(queryDb, seed);

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seed.TenantId,
            seed.ClinicId,
            search: "yılmaz",
            paymentsSearchLookupEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        result.Value.TotalAmount.Should().Be(150m);
    }

    [Fact]
    public async Task FlagOn_Should_FindReport_ByPetNameSearch_WhenPetReadModelPopulated()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQuerySideAsync(queryDb);
        var seed = await SeedPaymentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk", amount: 175m);
        await SeedPetReadModelAsync(queryDb, seed);

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seed.TenantId,
            seed.ClinicId,
            search: "pamuk",
            paymentsSearchLookupEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        result.Value.TotalAmount.Should().Be(175m);
    }

    [Fact]
    public async Task FlagOn_Should_ReturnOnlyRowsForRequestedTenant()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQuerySideAsync(queryDb);
        var seedA = await SeedPaymentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk", amount: 100m);
        await SeedPaymentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Other", amount: 200m);
        await SeedClientReadModelAsync(queryDb, seedA);

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seedA.TenantId,
            seedA.ClinicId,
            search: "yılmaz",
            paymentsSearchLookupEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        result.Value.TotalAmount.Should().Be(100m);
    }

    private static async Task ResetQuerySideAsync(QueryDbContext queryDb)
    {
        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await queryDb.ClientReadModels.ExecuteDeleteAsync();
        await queryDb.PetReadModels.ExecuteDeleteAsync();
    }

    private static async Task<PaymentReportSearchSeed> SeedPaymentAsync(
        AppDbContext commandDb,
        string clientFullName,
        string petName,
        decimal amount)
    {
        var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..20]);
        var clinic = new Clinic(tenant.Id, "Pilot Clinic", "Istanbul");
        commandDb.Tenants.Add(tenant);
        commandDb.Clinics.Add(clinic);
        await commandDb.SaveChangesAsync();

        var client = new Client(tenant.Id, clientFullName, "905551110088");
        commandDb.Clients.Add(client);
        await commandDb.SaveChangesAsync();

        var speciesId = await commandDb.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var speciesName = await commandDb.Species.Where(s => s.Id == speciesId).Select(s => s.Name).FirstAsync();
        var pet = new Pet(tenant.Id, client.Id, petName, speciesId);
        commandDb.Pets.Add(pet);
        await commandDb.SaveChangesAsync();

        var paidAt = DateTime.UtcNow.AddHours(-1);
        var payment = new Payment(
            tenant.Id,
            clinic.Id,
            client.Id,
            pet.Id,
            appointmentId: null,
            examinationId: null,
            amount,
            "TRY",
            PaymentMethod.Cash,
            paidAt,
            "Tahsilat");

        commandDb.Payments.Add(payment);
        await commandDb.SaveChangesAsync();

        return new PaymentReportSearchSeed(
            tenant.Id,
            clinic.Id,
            client.Id,
            pet.Id,
            payment.Id,
            paidAt,
            clientFullName,
            petName,
            speciesId,
            speciesName);
    }

    private static async Task SeedClientReadModelAsync(QueryDbContext queryDb, PaymentReportSearchSeed seed)
    {
        var now = DateTime.UtcNow;
        queryDb.ClientReadModels.Add(new ClientReadModel
        {
            ClientId = seed.ClientId,
            TenantId = seed.TenantId,
            FullName = seed.ClientFullName,
            FullNameNormalized = seed.ClientFullName.Trim().ToLowerInvariant(),
            Phone = "905551110088",
            PhoneNormalized = "905551110088",
            CreatedAtUtc = now,
            LastEventId = Guid.NewGuid(),
            LastProjectedAtUtc = now,
            LastEventOccurredAtUtc = now
        });
        await queryDb.SaveChangesAsync();
    }

    private static async Task SeedPetReadModelAsync(QueryDbContext queryDb, PaymentReportSearchSeed seed)
    {
        var now = DateTime.UtcNow;
        queryDb.PetReadModels.Add(new PetReadModel
        {
            PetId = seed.PetId,
            TenantId = seed.TenantId,
            ClientId = seed.ClientId,
            ClientFullName = seed.ClientFullName,
            ClientFullNameNormalized = seed.ClientFullName.Trim().ToLowerInvariant(),
            Name = seed.PetName,
            NameNormalized = seed.PetName.Trim().ToLowerInvariant(),
            SpeciesId = seed.SpeciesId,
            SpeciesName = seed.SpeciesName,
            SpeciesNameNormalized = seed.SpeciesName.Trim().ToLowerInvariant(),
            LastEventId = Guid.NewGuid(),
            LastProjectedAtUtc = now,
            LastEventOccurredAtUtc = now
        });
        await queryDb.SaveChangesAsync();
    }

    private static async Task<Result<PaymentReportResultDto>> InvokeHandlerAsync(
        IServiceProvider sp,
        Guid tenantId,
        Guid clinicId,
        string search,
        bool paymentsSearchLookupEnabled)
    {
        var from = DateTime.UtcNow.AddDays(-1);
        var to = DateTime.UtcNow.AddHours(1);

        var handler = new GetPaymentsReportQueryHandler(
            new FixedTenantContext(tenantId),
            new FixedClinicContext(clinicId),
            new PassthroughClinicReadScopeResolver(),
            sp.GetRequiredService<IReadRepository<Payment>>(),
            sp.GetRequiredService<IReadRepository<Client>>(),
            sp.GetRequiredService<IReadRepository<Pet>>(),
            sp.GetRequiredService<IReadRepository<Clinic>>(),
            sp.GetRequiredService<IClientReadModelLookupReader>(),
            sp.GetRequiredService<IPetReadModelLookupReader>(),
            Options.Create(new QueryReadModelsOptions
            {
                PaymentsSearchLookupEnabled = paymentsSearchLookupEnabled
            }));

        return await handler.Handle(
            new GetPaymentsReportQuery(from, to, clinicId, null, null, null, search, 1, 50),
            CancellationToken.None);
    }

    private sealed record PaymentReportSearchSeed(
        Guid TenantId,
        Guid ClinicId,
        Guid ClientId,
        Guid PetId,
        Guid PaymentId,
        DateTime PaidAtUtc,
        string ClientFullName,
        string PetName,
        Guid SpeciesId,
        string SpeciesName);

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
    }

    private sealed class FixedClinicContext(Guid clinicId) : IClinicContext
    {
        public Guid? ClinicId { get; } = clinicId;
    }

    private sealed class PassthroughClinicReadScopeResolver : Backend.Veteriner.Application.Clinics.Access.IClinicReadScopeResolver
    {
        public Task<Result<Backend.Veteriner.Application.Clinics.Access.ClinicReadScope>> ResolveAsync(
            Guid tenantId,
            Guid? requestClinicId,
            CancellationToken ct)
            => Task.FromResult(Result<Backend.Veteriner.Application.Clinics.Access.ClinicReadScope>.Success(
                new Backend.Veteriner.Application.Clinics.Access.ClinicReadScope(requestClinicId, null)));
    }
}
