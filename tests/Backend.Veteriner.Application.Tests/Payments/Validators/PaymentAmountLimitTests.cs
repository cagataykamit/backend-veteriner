using Backend.Veteriner.Application.Payments.Commands.Create;
using Backend.Veteriner.Application.Payments.Commands.Update;
using Backend.Veteriner.Domain.Payments;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Payments.Validators;

public sealed class PaymentAmountLimitTests
{
    [Fact]
    public void Create_Amount_Should_Be_Rejected_Above_Max_And_Accepted_AtMax()
    {
        var validator = new CreatePaymentCommandValidator();

        validator.Validate(CreateCmd(Payment.MaxAmount + 0.01m)).Errors
            .Should().Contain(e => e.PropertyName == nameof(CreatePaymentCommand.Amount));
        validator.Validate(CreateCmd(Payment.MaxAmount)).Errors
            .Should().NotContain(e => e.PropertyName == nameof(CreatePaymentCommand.Amount));
    }

    [Fact]
    public void Update_Amount_Should_Be_Rejected_Above_Max_And_Accepted_AtMax()
    {
        var validator = new UpdatePaymentCommandValidator();

        validator.Validate(UpdateCmd(Payment.MaxAmount + 0.01m)).Errors
            .Should().Contain(e => e.PropertyName == nameof(UpdatePaymentCommand.Amount));
        validator.Validate(UpdateCmd(Payment.MaxAmount)).Errors
            .Should().NotContain(e => e.PropertyName == nameof(UpdatePaymentCommand.Amount));
    }

    private static CreatePaymentCommand CreateCmd(decimal amount)
        => new(Guid.NewGuid(), Guid.NewGuid(), null, null, null, amount, "TRY", PaymentMethod.Cash, DateTime.UtcNow, null, Guid.NewGuid());
    private static UpdatePaymentCommand UpdateCmd(decimal amount)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, null, amount, "TRY", PaymentMethod.Cash, DateTime.UtcNow, null);
}
