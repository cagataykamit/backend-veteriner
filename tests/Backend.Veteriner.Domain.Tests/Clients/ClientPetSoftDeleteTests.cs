using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Pets;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Clients;

/// <summary>ADR-054: müşteri ve hasta yumuşak silme, engeller ve geri yükleme.</summary>
public sealed class ClientPetSoftDeleteTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();

    private static Client CreateClient() => new(Guid.NewGuid(), "Ayşe Yılmaz", Now.AddDays(-10), "05321234567");

    private static Pet CreatePet(Client client) => new(client.TenantId, client.Id, "Pamuk", Guid.NewGuid());

    [Fact]
    public void Client_Delete_Should_Mark_Deleted_And_Exclude_From_Reminders()
    {
        var client = CreateClient();

        client.Delete(UserId, Now, RecordDeletionBlockers.None).IsSuccess.Should().BeTrue();

        client.IsDeleted.Should().BeTrue();
        client.DeletedAtUtc.Should().Be(Now);
        client.DeletedByUserId.Should().Be(UserId);
        Client.NotDeleted.Compile()(client).Should().BeFalse();
        client.IsAcceptingReminders.Should().BeFalse();
        client.EnsureNotDeleted().Error.Code.Should().Be("Clients.Deleted");
    }

    [Fact]
    public void Client_Delete_Twice_Should_Keep_First_Deletion()
    {
        var client = CreateClient();
        client.Delete(UserId, Now, RecordDeletionBlockers.None);

        client.Delete(Guid.NewGuid(), Now.AddHours(1), RecordDeletionBlockers.None).IsSuccess.Should().BeTrue();

        client.DeletedAtUtc.Should().Be(Now);
        client.DeletedByUserId.Should().Be(UserId);
    }

    [Fact]
    public void Client_Delete_Should_Reject_WalkIn()
    {
        var walkIn = Client.CreateWalkIn(Guid.NewGuid(), Now);

        var result = walkIn.Delete(UserId, Now, RecordDeletionBlockers.None);

        result.Error.Code.Should().Be("Clients.WalkInReadOnly");
        walkIn.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Client_Delete_Should_Report_All_Blocking_Reasons()
    {
        var client = CreateClient();

        var result = client.Delete(UserId, Now, new RecordDeletionBlockers(HasOpenAccount: true, ActiveHospitalizationCount: 1, FutureAppointmentCount: 2));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.DeleteBlocked");
        result.Error.Message.Should().Contain("açık hesabı").And.Contain("aktif yatışı").And.Contain("2 gelecek randevusu");
        result.Error.Extensions!["reasons"].Should().BeEquivalentTo(new[]
        {
            RecordDeletionBlockers.OpenAccount,
            RecordDeletionBlockers.ActiveHospitalization,
            RecordDeletionBlockers.FutureAppointments,
        });
        result.Error.Extensions["futureAppointmentCount"].Should().Be(2);
        client.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Client_Restore_Should_Clear_Deletion()
    {
        var client = CreateClient();
        client.Delete(UserId, Now, RecordDeletionBlockers.None);

        client.Restore(Now.AddDays(30));

        client.IsDeleted.Should().BeFalse();
        client.DeletedByUserId.Should().BeNull();
        client.IsAcceptingReminders.Should().BeTrue();
        client.EnsureNotDeleted().IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Pet_Delete_Should_Block_Care_With_Deleted_Code()
    {
        var pet = CreatePet(CreateClient());

        pet.Delete(UserId, Now, RecordDeletionBlockers.None).IsSuccess.Should().BeTrue();

        pet.IsDeleted.Should().BeTrue();
        pet.DeletedWithClient.Should().BeFalse();
        Pet.ReceivesCare.Compile()(pet).Should().BeFalse();
        Pet.NotDeleted.Compile()(pet).Should().BeFalse();
        pet.EnsureCanStartCare().Error.Code.Should().Be("Pets.Deleted");
    }

    [Fact]
    public void Pet_Delete_Should_Be_Blocked_By_Active_Hospitalization_And_Future_Appointments()
    {
        var pet = CreatePet(CreateClient());

        var result = pet.Delete(UserId, Now, new RecordDeletionBlockers(false, 1, 1));

        result.Error.Code.Should().Be("Pets.DeleteBlocked");
        result.Error.Message.Should().StartWith("Hasta silinemez");
        pet.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Deceased_Pet_Can_Be_Deleted_And_Keeps_Status()
    {
        var pet = CreatePet(CreateClient());
        pet.SetStatus(PetStatus.Deceased, null, DateOnly.FromDateTime(Now));

        pet.Delete(UserId, Now, RecordDeletionBlockers.None).IsSuccess.Should().BeTrue();

        pet.Status.Should().Be(PetStatus.Deceased);
        pet.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void RestoreWithClient_Should_Restore_Only_Pets_Deleted_With_Client()
    {
        var client = CreateClient();
        var alone = CreatePet(client);
        var cascaded = CreatePet(client);
        alone.Delete(UserId, Now.AddDays(-1), RecordDeletionBlockers.None);
        client.Delete(UserId, Now, RecordDeletionBlockers.None);
        alone.DeleteWithClient(UserId, Now);
        cascaded.DeleteWithClient(UserId, Now);

        alone.DeletedWithClient.Should().BeFalse();
        alone.DeletedAtUtc.Should().Be(Now.AddDays(-1));
        cascaded.DeletedWithClient.Should().BeTrue();

        client.Restore(Now.AddDays(1));
        alone.RestoreWithClient();
        cascaded.RestoreWithClient();

        alone.IsDeleted.Should().BeTrue();
        cascaded.IsDeleted.Should().BeFalse();
        cascaded.DeletedWithClient.Should().BeFalse();
    }

    [Fact]
    public void Pet_Restore_Should_Be_Rejected_While_Client_Is_Deleted()
    {
        var pet = CreatePet(CreateClient());
        pet.Delete(UserId, Now, RecordDeletionBlockers.None);

        pet.Restore(clientIsDeleted: true).Error.Code.Should().Be("Pets.ClientDeleted");
        pet.IsDeleted.Should().BeTrue();

        pet.Restore(clientIsDeleted: false).IsSuccess.Should().BeTrue();
        pet.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Portal_Link_Should_Close_With_ClientDeleted_Reason()
    {
        var link = OwnerAccountClientLink.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        link.CloseByClientDeletion(UserId, Now).IsSuccess.Should().BeTrue();

        link.IsActive.Should().BeFalse();
        link.CloseReason.Should().Be(OwnerAccountClientLinkCloseReason.ClientDeleted);
        link.ClosedByUserId.Should().Be(UserId);
    }
}
