using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.IntegrationTests.Projections.Payments;

internal static class PaymentFinanceTestSupport
{
    public static async Task<PaymentSeed> SeedPaymentAsync(
        AppDbContext commandDb,
        Guid tenantId,
        Guid clinicId,
        decimal amount,
        DateTime paidAtUtc,
        string currency = "TRY")
    {
        var client = new Client(tenantId, $"Client-{Guid.NewGuid():N}"[..12], "905551110099");
        commandDb.Clients.Add(client);
        await commandDb.SaveChangesAsync();

        var speciesId = await commandDb.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
        var pet = new Pet(tenantId, client.Id, $"Pet-{Guid.NewGuid():N}"[..8], speciesId);
        commandDb.Pets.Add(pet);
        await commandDb.SaveChangesAsync();

        var payment = new Payment(
            tenantId,
            clinicId,
            client.Id,
            pet.Id,
            appointmentId: null,
            examinationId: null,
            amount,
            currency,
            PaymentMethod.Cash,
            paidAtUtc,
            notes: null);

        commandDb.Payments.Add(payment);
        await commandDb.SaveChangesAsync();

        return new PaymentSeed(
            payment.Id,
            tenantId,
            clinicId,
            amount,
            currency,
            paidAtUtc,
            OperationDayBounds.ToLocalDate(paidAtUtc));
    }

    public static async Task<(Guid TenantId, Guid ClinicA, Guid ClinicB)> SeedTenantWithTwoClinicsAsync(AppDbContext commandDb)
    {
        var tenant = new Tenant($"Tenant-{Guid.NewGuid():N}"[..16]);
        var clinicA = new Clinic(tenant.Id, "Clinic A", "Istanbul");
        var clinicB = new Clinic(tenant.Id, "Clinic B", "Ankara");
        commandDb.Tenants.Add(tenant);
        commandDb.Clinics.Add(clinicA);
        commandDb.Clinics.Add(clinicB);
        await commandDb.SaveChangesAsync();
        return (tenant.Id, clinicA.Id, clinicB.Id);
    }

    public sealed record PaymentSeed(
        Guid PaymentId,
        Guid TenantId,
        Guid ClinicId,
        decimal Amount,
        string Currency,
        DateTime PaidAtUtc,
        DateOnly LocalDate);
}
