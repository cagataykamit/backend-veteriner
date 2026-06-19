using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Domain.Clients;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Clients.IntegrationEvents;

public sealed class ClientProjectionSnapshotFactoryTests
{
    [Fact]
    public void Create_Should_MapAllFields_FromAggregate()
    {
        var tid = Guid.NewGuid();
        var client = new Client(tid, "  Ayşe Yılmaz  ", "05321234567", "Ayse@Example.com", "Ankara");

        var snap = ClientProjectionSnapshotFactory.Create(client);

        snap.ClientId.Should().Be(client.Id);
        snap.TenantId.Should().Be(tid);
        snap.FullName.Should().Be("Ayşe Yılmaz");
        snap.FullNameNormalized.Should().Be(Client.NormalizeFullNameForDuplicateCheck("Ayşe Yılmaz"));
        snap.Email.Should().Be("ayse@example.com");
        snap.Phone.Should().Be("905321234567");
        snap.PhoneNormalized.Should().Be("905321234567");
        snap.CreatedAtUtc.Should().Be(client.CreatedAtUtc);
    }

    [Fact]
    public void Create_Should_AllowNullOptionalFields()
    {
        var client = new Client(Guid.NewGuid(), "Mehmet Demir");

        var snap = ClientProjectionSnapshotFactory.Create(client);

        snap.Email.Should().BeNull();
        snap.Phone.Should().BeNull();
        snap.PhoneNormalized.Should().BeNull();
        snap.FullName.Should().Be("Mehmet Demir");
    }

    [Fact]
    public void Create_Should_BeDeterministic_ForSameAggregate()
    {
        var client = new Client(Guid.NewGuid(), "Ali Veli", "05321234567", "ali@example.com");

        var first = ClientProjectionSnapshotFactory.Create(client);
        var second = ClientProjectionSnapshotFactory.Create(client);

        second.Should().Be(first);
    }
}
