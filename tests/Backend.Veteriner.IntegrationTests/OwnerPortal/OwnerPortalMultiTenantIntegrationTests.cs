using System.Net.Http.Json;
using System.Text.Json;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.OwnerPortal;

/// <summary>
/// Portal JWT'sinde kiracı yoktur; kiracı bağdan çözülür (ADR-040). İki farklı kiracıdaki klinikte hayvanı olan sahip
/// iki bağı da görür ve her bağ yalnız kendi kiracısının hayvanını döndürür.
/// </summary>
public sealed partial class OwnerPortalIntegrationTests
{
    [Fact]
    public async Task Owner_With_Pets_In_Two_Tenants_Should_See_Both_Links_And_Each_Pet()
    {
        var staff = await SeedStaffAsync();
        var first = await InviteAndAcceptAsync(staff);

        Guid secondLinkId, firstPetId, secondPetId;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var accountId = await db.OwnerAccounts
                .Where(a => a.Email == OwnerAccount.NormalizeEmail(first.Email))
                .Select(a => a.Id)
                .SingleAsync();

            var otherTenant = new Tenant($"PortalC-{Guid.NewGuid():N}"[..20], now);
            var otherClinic = new Clinic(otherTenant.Id, $"Diğer-{Guid.NewGuid():N}"[..12], "İzmir");
            var otherClient = new Client(otherTenant.Id, "İkinci Klinik Kaydı", now, email: first.Email);
            var firstPet = new Pet(staff.TenantId, first.ClientId, "Birinci", speciesId);
            var secondPet = new Pet(otherTenant.Id, otherClient.Id, "İkinci", speciesId);
            var link = OwnerAccountClientLink.Create(accountId, otherTenant.Id, otherClinic.Id, otherClient.Id, Guid.NewGuid(), now);
            db.AddRange(otherTenant, otherClinic, otherClient, firstPet, secondPet, link);
            await db.SaveChangesAsync();

            secondLinkId = link.Id;
            firstPetId = firstPet.Id;
            secondPetId = secondPet.Id;
        }

        var me = await SendAsync(HttpMethod.Get, "/api/portal/v1/me", first.AccessToken);
        var meJson = await me.Content.ReadFromJsonAsync<JsonElement>();
        meJson.GetProperty("links").EnumerateArray().Select(l => l.GetProperty("linkId").GetGuid())
            .Should().BeEquivalentTo([first.LinkId, secondLinkId]);

        (await PetIdsAsync(first.LinkId, first.AccessToken)).Should().Equal(firstPetId);
        (await PetIdsAsync(secondLinkId, first.AccessToken)).Should().Equal(secondPetId);
    }

    private async Task<List<Guid>> PetIdsAsync(Guid linkId, string bearer)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/portal/v1/links/{linkId}/pets", bearer);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.EnumerateArray().Select(p => p.GetProperty("petId").GetGuid()).ToList();
    }
}
