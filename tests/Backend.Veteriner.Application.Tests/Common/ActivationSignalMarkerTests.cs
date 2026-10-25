using Backend.Veteriner.Application.Appointments.Commands.Create;
using Backend.Veteriner.Application.Charges.Commands.Create;
using Backend.Veteriner.Application.Clients.Commands.Create;
using Backend.Veteriner.Application.Clients.Commands.QuickRegister;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Commands.Create;
using Backend.Veteriner.Application.Payments.Commands.Create;
using Backend.Veteriner.Application.Pets.Commands.Create;
using Backend.Veteriner.Application.Products.Commands.Create;
using Backend.Veteriner.Application.Sales.Commands.Create;
using Backend.Veteriner.Application.Vaccinations.Commands.Create;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Common;

/// <summary>
/// TRIAL-008: "anlamlı aksiyon" komutları işaretlidir ve tür eşlemesi sözleşme §2.1 ile aynıdır. İşaret yanlışlıkla
/// kaldırılırsa aktivasyon ölçümü sessizce boş kalır; bu test onu engeller.
/// </summary>
public sealed class ActivationSignalMarkerTests
{
    [Theory]
    [InlineData(typeof(CreateClientCommand), ActivationSignalKind.ClientCreated)]
    [InlineData(typeof(QuickRegisterClientCommand), ActivationSignalKind.ClientCreated)]
    [InlineData(typeof(CreatePetCommand), ActivationSignalKind.PetCreated)]
    [InlineData(typeof(CreateAppointmentCommand), ActivationSignalKind.AppointmentCreated)]
    [InlineData(typeof(CreateExaminationCommand), ActivationSignalKind.ExaminationCreated)]
    [InlineData(typeof(CreateVaccinationCommand), ActivationSignalKind.VaccinationCreated)]
    [InlineData(typeof(CreateChargeCommand), ActivationSignalKind.ChargeCreated)]
    [InlineData(typeof(CreatePaymentCommand), ActivationSignalKind.PaymentCreated)]
    [InlineData(typeof(CreateSaleCommand), ActivationSignalKind.SaleCreated)]
    [InlineData(typeof(CreateProductCommand), ActivationSignalKind.ProductCreated)]
    public void MeaningfulActionCommand_Should_Carry_ActivationSignal(Type commandType, ActivationSignalKind expected)
    {
        typeof(IActivationSignalRequest).IsAssignableFrom(commandType)
            .Should().BeTrue($"{commandType.Name} deneme aktivasyonunda anlamlı aksiyondur");

        var instance = (IActivationSignalRequest)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(commandType);
        instance.ActivationSignal.Should().Be(expected);
    }
}
