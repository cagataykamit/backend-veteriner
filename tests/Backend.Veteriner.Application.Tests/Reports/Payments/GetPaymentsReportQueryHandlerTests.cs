using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Reports.Payments;
using Backend.Veteriner.Application.Reports.Payments.Queries.GetPaymentReport;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Payments;

public sealed class GetPaymentsReportQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Clients.Client>> _clients = new();
    private readonly Mock<IReadRepository<Backend.Veteriner.Domain.Pets.Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private GetPaymentsReportQueryHandler CreateHandler()
        => new(_tenant.Object, _clinic.Object, _payments.Object, _clients.Object, _pets.Object, _clinics.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var cmd = new GetPaymentsReportQuery(
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow,
            null,
            null,
            null,
            null,
            null,
            1,
            20);

        var r = await CreateHandler().Handle(cmd, CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_FromAfterTo()
    {
        var tid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var cmd = new GetPaymentsReportQuery(
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(-2),
            null,
            null,
            null,
            null,
            null,
            1,
            20);

        var r = await CreateHandler().Handle(cmd, CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Payments.ReportDateRangeInvalid");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_RangeTooLong()
    {
        var tid = Guid.Parse("22222222-2222-2222-2222-222222222222");
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var from = DateTime.UtcNow.AddDays(-200);
        var to = DateTime.UtcNow;
        var cmd = new GetPaymentsReportQuery(from, to, null, null, null, null, null, 1, 20);

        var r = await CreateHandler().Handle(cmd, CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Payments.ReportRangeTooLong");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ClinicNotInTenant()
    {
        var tid = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var cid = Guid.Parse("44444444-4444-4444-4444-444444444444");
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var cmd = new GetPaymentsReportQuery(
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow,
            cid,
            null,
            null,
            null,
            null,
            1,
            20);

        var r = await CreateHandler().Handle(cmd, CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ApplyMethodFilter_And_ReturnTotals()
    {
        var tid = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var clinicId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));

        var from = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 4, 30, 23, 59, 59, DateTimeKind.Utc);

        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredAmountsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<decimal> { 10m, 25.50m });

        var p1 = CreatePaymentEntity(tid, clinicId, null, 10m, PaymentMethod.Cash, from.AddHours(1));
        var p2 = CreatePaymentEntity(tid, clinicId, null, 25.50m, PaymentMethod.Cash, from.AddHours(2));
        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { p2, p1 });

        _clients
            .Setup(r => r.ListAsync(It.IsAny<Backend.Veteriner.Application.Clients.Specs.ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Backend.Veteriner.Domain.Clients.Client>());
        _pets
            .Setup(r => r.ListAsync(It.IsAny<Backend.Veteriner.Application.Pets.Specs.PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Backend.Veteriner.Domain.Pets.Pet>());

        _clinics
            .Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());

        var cmd = new GetPaymentsReportQuery(
            from,
            to,
            null,
            PaymentMethod.Cash,
            null,
            null,
            null,
            1,
            20);

        var r = await CreateHandler().Handle(cmd, CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        r.Value!.TotalCount.Should().Be(2);
        r.Value.TotalAmount.Should().Be(35.50m);
        r.Value.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_Should_UseExplicitClinicId_WhenJwtClinicMissing()
    {
        var tid = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var clinicId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "X", "Y"));

        var from = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc);

        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredAmountsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<decimal>());
        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());

        var cmd = new GetPaymentsReportQuery(from, to, clinicId, null, null, null, null, 1, 20);
        var r = await CreateHandler().Handle(cmd, CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        _payments.Verify(
            x => x.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_FilterByClientId()
    {
        var tid = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var clinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var clientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "M"));

        var from = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc);

        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredAmountsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<decimal> { 5m });
        var p = CreatePaymentEntity(tid, clinicId, clientId, 5m, PaymentMethod.Transfer, from.AddHours(1));
        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { p });

        _clients
            .Setup(r => r.ListAsync(It.IsAny<Backend.Veteriner.Application.Clients.Specs.ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Backend.Veteriner.Domain.Clients.Client>());
        _pets
            .Setup(r => r.ListAsync(It.IsAny<Backend.Veteriner.Application.Pets.Specs.PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Backend.Veteriner.Domain.Pets.Pet>());
        _clinics
            .Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());

        var cmd = new GetPaymentsReportQuery(from, to, null, null, clientId, null, null, 1, 20);
        var r = await CreateHandler().Handle(cmd, CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        r.Value!.TotalCount.Should().Be(1);
        r.Value.TotalAmount.Should().Be(5m);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ClinicContextMismatch()
    {
        var tid = Guid.NewGuid();
        var jwtClinic = Guid.NewGuid();
        var bodyClinic = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(jwtClinic);

        var cmd = new GetPaymentsReportQuery(
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow,
            bodyClinic,
            null,
            null,
            null,
            null,
            1,
            20);

        var r = await CreateHandler().Handle(cmd, CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Payments.ClinicContextMismatch");
    }

    private static Payment CreatePaymentEntity(
        Guid tid,
        Guid clinicId,
        Guid? clientId,
        decimal amount,
        PaymentMethod method,
        DateTime paidAt)
    {
        return new Payment(
            tid,
            clinicId,
            clientId ?? Guid.NewGuid(),
            null,
            null,
            null,
            amount,
            "TRY",
            method,
            paidAt,
            null);
    }
}
