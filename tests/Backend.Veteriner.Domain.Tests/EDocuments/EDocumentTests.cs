using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.EDocuments;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.EDocuments;

/// <summary>ADR-036 Dilim 3–4: belge türü kuralı, KDV dahil tutar, durum makinesi, alıcı ve ücret bağı.</summary>
public sealed class EDocumentTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ClinicTaxpayerType.SelfEmployed, ChargeLineKind.Service, false, EDocumentType.ESmm)]
    [InlineData(ClinicTaxpayerType.SelfEmployed, ChargeLineKind.Product, true, EDocumentType.ESmm)]
    [InlineData(ClinicTaxpayerType.Company, ChargeLineKind.Service, false, EDocumentType.EArchive)]
    [InlineData(ClinicTaxpayerType.Company, ChargeLineKind.Product, true, EDocumentType.EInvoice)]
    [InlineData(ClinicTaxpayerType.Both, ChargeLineKind.Service, true, EDocumentType.ESmm)]
    [InlineData(ClinicTaxpayerType.Both, ChargeLineKind.Product, false, EDocumentType.EArchive)]
    [InlineData(ClinicTaxpayerType.Both, ChargeLineKind.Product, true, EDocumentType.EInvoice)]
    public void TypeRule_Should_Follow_Taxpayer_Line_And_Buyer(
        ClinicTaxpayerType taxpayer, ChargeLineKind line, bool eInvoiceUser, EDocumentType expected)
        => EDocumentTypeRule.Resolve(taxpayer, line, eInvoiceUser).Should().Be(expected);

    [Theory]
    [InlineData(1200, 20, null, 1000)]
    [InlineData(1100, 10, null, 1000)]
    [InlineData(100, 0, null, 100)]
    [InlineData(1000, 20, 20.0, 1000)]
    [InlineData(999.99, 20, null, 833.33)]
    public void NetLineAmount_Should_Remove_Vat_And_Withholding(decimal gross, decimal vat, double? withholding, decimal expected)
        => EDocumentAmounts.NetLineAmount(gross, vat, (decimal?)withholding).Should().Be(expected);

    [Fact]
    public void NetUnitPrice_Should_Divide_By_Quantity_With_Six_Decimals()
        => EDocumentAmounts.NetUnitPrice(1000m, 3m).Should().Be(333.333333m);

    [Fact]
    public void Withholding_Should_Apply_Only_To_Corporate_ESmm()
    {
        EDocumentRules.AppliesWithholding(EDocumentType.ESmm, EDocumentBuyerKind.Corporate).Should().BeTrue();
        EDocumentRules.AppliesWithholding(EDocumentType.ESmm, EDocumentBuyerKind.Individual).Should().BeFalse();
        EDocumentRules.AppliesWithholding(EDocumentType.EArchive, EDocumentBuyerKind.Corporate).Should().BeFalse();
    }

    [Fact]
    public void Individual_Buyer_Without_NationalId_Should_Use_Unknown_Buyer_Placeholder()
    {
        var buyer = EDocumentBuyer.Individual("Ayşe Yılmaz", null, null, null, null, null).Value!;

        buyer.IsUnknownBuyer.Should().BeTrue();
        buyer.IdentityNumber.Should().Be(EDocumentRules.UnknownBuyerNationalId);
        buyer.Name.Should().Be("Ayşe Yılmaz");
    }

    [Fact]
    public void Corporate_Buyer_Should_Require_Valid_Tax_Number()
    {
        EDocumentBuyer.Corporate("Örnek Ltd.", null, null, null, null, null, null).Error.Code
            .Should().Be("EDocuments.BuyerIdentityMissing");
        EDocumentBuyer.Corporate("Örnek Ltd.", "1234567890", "Kadıköy", null, null, null, null).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_Should_Start_As_Draft_With_Total_And_External_Reference()
    {
        var doc = NewDocument();

        doc.Status.Should().Be(EDocumentStatus.Draft);
        doc.TotalAmount.Should().Be(1500m);
        doc.ExternalReference.Should().NotBe(Guid.Empty);
        doc.Lines.Select(l => l.Position).Should().Equal(1, 2);
        doc.HoldsCharges.Should().BeTrue();
    }

    [Fact]
    public void Withholding_Should_Be_Dropped_For_Individual_Buyer()
        => NewDocument(withholding: 20m).WithholdingRate.Should().BeNull();

    [Fact]
    public void Send_Issue_Cancel_Flow_Should_Release_Charges_Only_At_End()
    {
        var doc = NewDocument();

        doc.BeginSending(Now).IsSuccess.Should().BeTrue();
        doc.AttemptCount.Should().Be(1);
        doc.AwaitProviderResult("job-1", Now.AddSeconds(10), Now).IsSuccess.Should().BeTrue();
        doc.Status.Should().Be(EDocumentStatus.Sending);
        doc.MarkIssued("doc-1", "GIB2026000000001", "uuid", 1500m, Now).IsSuccess.Should().BeTrue();
        doc.Status.Should().Be(EDocumentStatus.Issued);
        doc.NextCheckAtUtc.Should().BeNull();

        doc.RequestCancel(UserId, "Yanlış müşteri", Now.AddDays(1)).IsSuccess.Should().BeTrue();
        doc.HoldsCharges.Should().BeTrue();
        doc.MarkCancelled(Now.AddDays(1)).IsSuccess.Should().BeTrue();
        doc.HoldsCharges.Should().BeFalse();
    }

    [Fact]
    public void Cancel_Should_Be_Rejected_After_Window()
    {
        var doc = Issued();

        var result = doc.RequestCancel(UserId, "Geç iptal", Now.AddDays(EDocumentRules.CancelWindowDays).AddMinutes(1));

        result.Error.Code.Should().Be("EDocuments.CancelWindowExpired");
        doc.Status.Should().Be(EDocumentStatus.Issued);
    }

    [Fact]
    public void Cancel_Should_Require_Reason()
        => Issued().RequestCancel(UserId, "  ", Now).Error.Code.Should().Be("EDocuments.Validation");

    [Fact]
    public void Failed_Document_Can_Be_Retried_Or_Discarded()
    {
        var doc = NewDocument();
        doc.BeginSending(Now);
        doc.MarkFailed(EDocumentErrorKind.InvalidData, "Alıcı adresi eksik", "422", Now).IsSuccess.Should().BeTrue();
        doc.LastErrorKind.Should().Be(EDocumentErrorKind.InvalidData);

        doc.BeginSending(Now).IsSuccess.Should().BeTrue();
        doc.AttemptCount.Should().Be(2);
        doc.LastErrorMessage.Should().BeNull();

        doc.MarkFailed(EDocumentErrorKind.QuotaExceeded, "Kontör yok", null, Now);
        doc.Discard(UserId, "Elle kesildi", Now).IsSuccess.Should().BeTrue();
        doc.Status.Should().Be(EDocumentStatus.Discarded);
        doc.HoldsCharges.Should().BeFalse();
    }

    [Fact]
    public void Issued_Document_Cannot_Be_Discarded_Or_Resent()
    {
        var doc = Issued();

        doc.Discard(UserId, "x", Now).Error.Code.Should().Be("EDocuments.InvalidState");
        doc.BeginSending(Now).Error.Code.Should().Be("EDocuments.InvalidState");
    }

    [Fact]
    public void Transient_Failure_Should_Keep_Sending_With_Next_Check()
    {
        var doc = NewDocument();
        doc.BeginSending(Now);

        doc.RecordTransientFailure("Zaman aşımı", null, Now.AddMinutes(1), Now).IsSuccess.Should().BeTrue();

        doc.Status.Should().Be(EDocumentStatus.Sending);
        doc.NextCheckAtUtc.Should().Be(Now.AddMinutes(1));
        doc.LastErrorKind.Should().Be(EDocumentErrorKind.Transient);
    }

    [Fact]
    public void Cancel_Rejected_Should_Allow_New_Cancel_Request()
    {
        var doc = Issued();
        doc.RequestCancel(UserId, "İptal", Now);
        doc.MarkCancelRejected("Süre doldu", "422", Now).IsSuccess.Should().BeTrue();

        doc.RequestCancel(UserId, "Tekrar", Now).IsSuccess.Should().BeTrue();
        doc.Status.Should().Be(EDocumentStatus.CancelRequested);
    }

    [Fact]
    public void Charge_Should_Be_Bound_To_Single_Document_And_Locked_While_Bound()
    {
        var charge = Charge.Create(TenantId, ClinicId, ClientId, null, null, null, "Muayene", 1m, 1000m, UserId, Now, null).Value!;
        var first = Guid.NewGuid();

        charge.AttachEDocument(first).IsSuccess.Should().BeTrue();
        charge.AttachEDocument(first).IsSuccess.Should().BeTrue();
        charge.AttachEDocument(Guid.NewGuid()).Error.Code.Should().Be("EDocuments.ChargeAlreadyDocumented");
        charge.Correct("Muayene 2", 1m, 900m, "Fiyat yanlış").Error.Code.Should().Be("Charges.EDocumentAttached");
        charge.Void(UserId, "Yanlış ücret", Now).Error.Code.Should().Be("Charges.EDocumentAttached");

        charge.ReleaseEDocument(first);
        charge.EDocumentId.Should().BeNull();
        charge.Void(UserId, "Yanlış ücret", Now).IsSuccess.Should().BeTrue();
        charge.AttachEDocument(Guid.NewGuid()).Error.Code.Should().Be("EDocuments.ChargeVoided");
    }

    [Fact]
    public void Connection_Should_Require_Account_Selection_When_Account_Unknown()
    {
        var connection = EDocumentConnection.Create(TenantId, ClinicId, "parasut", EDocumentEnvironment.Test, Now);
        connection.Authorize("parasut", EDocumentEnvironment.Test, "sifreli", null, null, UserId, Now);
        connection.Status.Should().Be(EDocumentConnectionStatus.AccountSelectionRequired);
        connection.IsUsable.Should().BeFalse();

        connection.SelectAccount("123", "Örnek Klinik", Now).IsSuccess.Should().BeTrue();
        connection.IsUsable.Should().BeTrue();

        connection.RequireReauthorization("Yetki bitti", Now);
        connection.Status.Should().Be(EDocumentConnectionStatus.ReauthorizationRequired);
        connection.ProtectedCredentials.Should().BeNull();

        connection.Disconnect(Now);
        connection.Status.Should().Be(EDocumentConnectionStatus.NotConnected);
        connection.AccountId.Should().BeNull();
    }

    private static EDocument Issued()
    {
        var doc = NewDocument();
        doc.BeginSending(Now);
        doc.MarkIssued("doc-1", "1", "uuid", null, Now);
        return doc;
    }

    private static EDocument NewDocument(decimal? withholding = null)
        => EDocument.Create(
            TenantId,
            ClinicId,
            ClientId,
            null,
            null,
            EDocumentType.ESmm,
            EDocumentEnvironment.Test,
            "parasut",
            Guid.NewGuid(),
            new string('A', 64),
            null,
            EDocumentBuyer.Individual("Ayşe Yılmaz", "10000000146", null, null, null, null).Value!,
            [
                new EDocumentLineDraft(Guid.NewGuid(), "Muayene", 1m, null, 1200m, 20m, ChargeLineKind.Service),
                new EDocumentLineDraft(Guid.NewGuid(), "Aşı", 1m, null, 300m, 20m, ChargeLineKind.Service),
            ],
            withholding,
            UserId,
            Now).Value!;
}
