using Backend.Veteriner.Domain.Clients;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Clients;

/// <summary>Müşteri notu (PET_STATUS_CLIENT_CONTACT_API_CONTRACT.md §2.1).</summary>
public sealed class ClientNotesTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);

    private static Client CreateClient() => new(Guid.NewGuid(), "Deneme Müşteri", Now);

    [Fact]
    public void ApplyNotes_Should_Trim_And_Store()
    {
        var client = CreateClient();

        client.ApplyNotes("  Akşam aranmak istiyor  ").IsSuccess.Should().BeTrue();

        client.Notes.Should().Be("Akşam aranmak istiyor");
    }

    [Fact]
    public void ApplyNotes_Null_Should_Not_Touch_Existing_Note()
    {
        var client = CreateClient();
        client.ApplyNotes("İlk not");

        client.ApplyNotes(null).IsSuccess.Should().BeTrue();

        client.Notes.Should().Be("İlk not");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplyNotes_Empty_Should_Clear(string empty)
    {
        var client = CreateClient();
        client.ApplyNotes("İlk not");

        client.ApplyNotes(empty).IsSuccess.Should().BeTrue();

        client.Notes.Should().BeNull();
    }

    [Fact]
    public void ApplyNotes_Should_Accept_Max_Length_And_Reject_Longer()
    {
        var client = CreateClient();

        client.ApplyNotes(new string('a', Client.MaxNotesLength)).IsSuccess.Should().BeTrue();

        var tooLong = client.ApplyNotes(new string('b', Client.MaxNotesLength + 1));
        tooLong.IsSuccess.Should().BeFalse();
        tooLong.Error.Code.Should().Be("Clients.Validation");
        client.Notes.Should().HaveLength(Client.MaxNotesLength).And.StartWith("a");
    }
}
