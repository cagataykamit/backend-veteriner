using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Payments.Queries.GetList;
using Backend.Veteriner.Application.Payments.ReadModels;
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
/// CQRS-12D-7 smoke: payment list Strategy B shared search lookup (list only).
/// </summary>
[Collection("pet-projection")]
public sealed class PaymentListSearchLookupSmokeIntegrationTests
{
    private readonly PetProjectionWebApplicationFactory _factory;

    public PaymentListSearchLookupSmokeIntegrationTests(PetProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task FlagOff_Should_FindPayment_ByClientNameSearch_EvenWhenQueryDbEmpty()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQuerySideAsync(queryDb);
        var seed = await SeedPaymentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk");

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seed.TenantId,
            seed.ClinicId,
            search: "yılmaz",
            paymentsSearchLookupEnabled: false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(1);
        result.Value.Items.Should().ContainSingle(x => x.Id == seed.PaymentId);
    }

    [Fact]
    public async Task FlagOn_Should_NotFallback_WhenReadModelsEmpty()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQuerySideAsync(queryDb);
        await SeedPaymentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk");
        var seed = await SeedPaymentAsync(commandDb, clientFullName: "Mehmet Demir", petName: "Boncuk");

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seed.TenantId,
            seed.ClinicId,
            search: "yılmaz",
            paymentsSearchLookupEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task FlagOn_Should_FindPayment_ByClientNameSearch_WhenClientReadModelPopulated()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQuerySideAsync(queryDb);
        var seed = await SeedPaymentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk");
        await SeedClientReadModelAsync(queryDb, seed);

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seed.TenantId,
            seed.ClinicId,
            search: "yılmaz",
            paymentsSearchLookupEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(1);
        result.Value.Items.Should().ContainSingle(x => x.Id == seed.PaymentId);
    }

    [Fact]
    public async Task FlagOn_Should_FindPayment_ByPetNameSearch_WhenPetReadModelPopulated()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQuerySideAsync(queryDb);
        var seed = await SeedPaymentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk");
        await SeedPetReadModelAsync(queryDb, seed);

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seed.TenantId,
            seed.ClinicId,
            search: "pamuk",
            paymentsSearchLookupEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(1);
        result.Value.Items.Should().ContainSingle(x => x.Id == seed.PaymentId);
    }

    [Fact]
    public async Task FlagOn_Should_ReturnOnlyRowsForRequestedTenant()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();

        await ResetQuerySideAsync(queryDb);
        var seedA = await SeedPaymentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Pamuk");
        var seedB = await SeedPaymentAsync(commandDb, clientFullName: "Ayşe Yılmaz", petName: "Other");
        await SeedClientReadModelAsync(queryDb, seedA);

        var result = await InvokeHandlerAsync(
            scope.ServiceProvider,
            seedA.TenantId,
            seedA.ClinicId,
            search: "yılmaz",
            paymentsSearchLookupEnabled: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(1);
        result.Value.Items.Should().ContainSingle(x => x.Id == seedA.PaymentId);
        result.Value.Items.Should().NotContain(x => x.Id == seedB.PaymentId);
    }

    private static async Task ResetQuerySideAsync(QueryDbContext queryDb)
    {
        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await queryDb.ClientReadModels.ExecuteDeleteAsync();
        await queryDb.PetReadModels.ExecuteDeleteAsync();
    }

    private static async Task<PaymentSearchSeed> SeedPaymentAsync(
        AppDbContext commandDb,
        string clientFullName,
        string petName)
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

        var payment = new Payment(
            tenant.Id,
            clinic.Id,
            client.Id,
            pet.Id,
            appointmentId: null,
            examinationId: null,
            amount: 150m,
            currency: "TRY",
            PaymentMethod.Cash,
            DateTime.UtcNow.AddHours(-1),
            "Tahsilat");

        commandDb.Payments.Add(payment);
        await commandDb.SaveChangesAsync();

        return new PaymentSearchSeed(
            tenant.Id,
            clinic.Id,
            client.Id,
            pet.Id,
            payment.Id,
            clientFullName,
            petName,
            speciesId,
            speciesName);
    }

    private static async Task SeedClientReadModelAsync(QueryDbContext queryDb, PaymentSearchSeed seed)
    {
        var now = DateTime.UtcNow;
        queryDb.ClientReadModels.Add(new ClientReadModel
        {
            ClientId = seed.ClientId,
            TenantId = seed.TenantId,
            FullName = seed.ClientFullName,
            FullNameNormalized = seed.ClientFullName.Trim().ToLowerInvariant(),
            Email = null,
            Phone = "905551110088",
            PhoneNormalized = "905551110088",
            CreatedAtUtc = now,
            LastEventId = Guid.NewGuid(),
            LastProjectedAtUtc = now,
            LastEventOccurredAtUtc = now
        });
        await queryDb.SaveChangesAsync();
    }

    private static async Task SeedPetReadModelAsync(QueryDbContext queryDb, PaymentSearchSeed seed)
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

    private static async Task<Result<PagedResult<Backend.Veteriner.Application.Payments.Contracts.Dtos.PaymentListItemDto>>> InvokeHandlerAsync(
        IServiceProvider sp,
        Guid tenantId,
        Guid clinicId,
        string search,
        bool paymentsSearchLookupEnabled)
    {
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
                PaymentsSearchLookupEnabled = paymentsSearchLookupEnabled
            }));

        return await handler.Handle(
            new GetPaymentsListQuery(
                new PaymentListPagingRequest { Page = 1, PageSize = 50 },
                clinicId,
                Search: search),
            CancellationToken.None);
    }

    private sealed record PaymentSearchSeed(
        Guid TenantId,
        Guid ClinicId,
        Guid ClientId,
        Guid PetId,
        Guid PaymentId,
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
