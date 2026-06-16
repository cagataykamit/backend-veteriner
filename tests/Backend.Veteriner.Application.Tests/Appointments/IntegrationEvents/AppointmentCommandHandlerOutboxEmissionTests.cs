using Backend.Veteriner.Application.Appointments.Commands.Create;
using Backend.Veteriner.Application.Appointments.Commands.Update;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Appointments.Commands.Cancel;
using Backend.Veteriner.Application.Appointments.Commands.Complete;
using Backend.Veteriner.Application.Appointments.Commands.Reschedule;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Appointments.IntegrationEvents;

public sealed class AppointmentCommandHandlerOutboxEmissionTests
{
    [Theory]
    [InlineData(typeof(CreateAppointmentCommandHandler))]
    [InlineData(typeof(UpdateAppointmentCommandHandler))]
    [InlineData(typeof(RescheduleAppointmentCommandHandler))]
    [InlineData(typeof(CancelAppointmentCommandHandler))]
    [InlineData(typeof(CompleteAppointmentCommandHandler))]
    public void Handler_Should_NotDependOnAppointmentIntegrationEventOutbox(Type handlerType)
    {
        var ctor = handlerType.GetConstructors().Single();
        ctor.GetParameters()
            .Select(p => p.ParameterType)
            .Should()
            .NotContain(
                typeof(IAppointmentIntegrationEventOutbox),
                "CQRS-4 fazinda command handler'lar henuz appointment integration event uretmemeli");
    }
}
