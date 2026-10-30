using Backend.Veteriner.Domain.Prescriptions;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Prescriptions;

/// <summary>RECORD-008: gerekçeli iptal (silme yok), sonradan ekleme işareti ve kararlı satır kimlikleri.</summary>
public sealed class PrescriptionCancellationTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

    private static Prescription NewPrescription()
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, Now.AddHours(-1), "Reçete", "İçerik", null, null, Now);

    [Fact]
    public void Cancel_requires_reason_and_keeps_who_when_why()
    {
        var p = NewPrescription();
        var user = Guid.NewGuid();

        p.Cancel(user, "kısa", Now).Error.Code.Should().Be("Prescriptions.Validation");
        p.IsCancelled.Should().BeFalse();

        p.Cancel(user, "  Yanlış hastaya yazıldı ", Now).IsSuccess.Should().BeTrue();
        p.IsCancelled.Should().BeTrue();
        p.CancelledByUserId.Should().Be(user);
        p.CancelReason.Should().Be("Yanlış hastaya yazıldı");
        p.CancelledAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Cancelled_prescription_cannot_be_cancelled_again_or_changed()
    {
        var p = NewPrescription();
        p.Cancel(Guid.NewGuid(), "Yanlış hastaya yazıldı", Now).IsSuccess.Should().BeTrue();

        p.Cancel(Guid.NewGuid(), "Tekrar iptal ediliyor", Now).Error.Code.Should().Be("Prescriptions.AlreadyCancelled");
        p.UpdateDetails(p.ClinicId, p.PetId, null, null, Now, "Yeni", "İçerik", null, null, Now)
            .Error.Code.Should().Be("Prescriptions.AlreadyCancelled");
        p.ReplaceItems([new PrescriptionItemDraft("X", null, null, null)]).Error.Code.Should().Be("Prescriptions.AlreadyCancelled");
    }

    [Fact]
    public void Late_entry_requires_reason()
    {
        var p = NewPrescription();

        p.MarkAddedAfterFinalization(null).Error.Code.Should().Be(Prescription.LateEntryReasonRequiredCode);
        p.AddedAfterFinalization.Should().BeFalse();

        p.MarkAddedAfterFinalization(" Hekim sonradan ekledi ").IsSuccess.Should().BeTrue();
        p.AddedAfterFinalization.Should().BeTrue();
        p.LateEntryReason.Should().Be("Hekim sonradan ekledi");
    }

    [Fact]
    public void Items_with_id_are_updated_in_place_and_keep_their_identity()
    {
        var p = NewPrescription();
        p.ReplaceItems([new PrescriptionItemDraft("A", null, null, 1m), new PrescriptionItemDraft("B", null, null, 2m)]).IsSuccess.Should().BeTrue();
        var a = p.Items.Single(i => i.Name == "A").Id;

        p.ReplaceItems([new PrescriptionItemDraft("C", null, null, 3m), new PrescriptionItemDraft("A2", null, null, 5m, Id: a)]).IsSuccess.Should().BeTrue();

        p.Items.Should().HaveCount(2);
        var kept = p.Items.Single(i => i.Id == a);
        kept.Name.Should().Be("A2");
        kept.Quantity.Should().Be(5m);
        kept.SortOrder.Should().Be(2);
        p.Items.Single(i => i.Id != a).Name.Should().Be("C");
    }

    [Fact]
    public void Unknown_or_duplicate_item_id_is_rejected_without_change()
    {
        var p = NewPrescription();
        p.ReplaceItems([new PrescriptionItemDraft("A", null, null, 1m)]).IsSuccess.Should().BeTrue();
        var a = p.Items.Single().Id;

        p.ReplaceItems([new PrescriptionItemDraft("X", null, null, null, Id: Guid.NewGuid())]).Error.Code.Should().Be("Prescriptions.Validation");
        p.ReplaceItems([new PrescriptionItemDraft("X", null, null, null, Id: a), new PrescriptionItemDraft("Y", null, null, null, Id: a)])
            .Error.Code.Should().Be("Prescriptions.Validation");
        p.Items.Should().ContainSingle().Which.Name.Should().Be("A");
    }
}
