namespace Backend.Veteriner.Infrastructure.Projections.Appointments;

internal sealed class AppointmentProjectionMetadataMismatchException : Exception
{
    public AppointmentProjectionMetadataMismatchException(string message)
        : base(message)
    {
    }
}
