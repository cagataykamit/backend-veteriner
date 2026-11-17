using Backend.Veteriner.Application.AppointmentRequests.Portal;
using Backend.Veteriner.Application.AppointmentRequests.Public;
using Backend.Veteriner.Application.Appointments.Commands.Create;
using Backend.Veteriner.Application.Appointments.Commands.Reschedule;
using Backend.Veteriner.Application.Appointments.Commands.RevertCancellation;
using Backend.Veteriner.Application.Appointments.Commands.Update;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Waitlist;
using AwesomeAssertions;
using Mediator;

namespace Backend.Veteriner.Application.Tests.Common;

/// <summary>
/// <see cref="ITransactionLock"/> açık transaction ister (yoksa istisna, 500). Kilidi alan her komut <see cref="ITransactionalRequest"/>
/// olmalı: doğrudan alan handler'lar yansımayla bulunur, paylaşılan kural üzerinden alanlar (takvim kilidi, talep sınırı) listededir.
/// </summary>
public sealed class TransactionLockArchitectureTests
{
    /// <summary><c>AppointmentAssignment.LockScheduleAsync</c> ya da <c>AppointmentRequestIntake.EnsureBookableAsync</c> üzerinden kilit alan komutlar.</summary>
    private static readonly Type[] IndirectLockRequests =
    [
        typeof(CreateAppointmentCommand),
        typeof(UpdateAppointmentCommand),
        typeof(RescheduleAppointmentCommand),
        typeof(RevertAppointmentCancellationCommand),
        typeof(AcceptWaitlistOfferByLinkCommand),
        typeof(CreatePortalAppointmentRequestCommand),
        typeof(CreateOnlineBookingRequestCommand),
    ];

    [Fact]
    public void Requests_Whose_Handlers_Take_A_Transaction_Lock_Should_Be_Transactional()
    {
        var direct = typeof(ITransactionLock).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                && t.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(ITransactionLock))))
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
            .Select(i => i.GetGenericArguments()[0])
            .ToList();

        direct.Should().NotBeEmpty();
        direct.Concat(IndirectLockRequests)
            .Where(r => !typeof(ITransactionalRequest).IsAssignableFrom(r))
            .Select(r => r.Name)
            .Should().BeEmpty("kilit transaction ister; işaretsiz komutta istek 500 döner");
    }
}
