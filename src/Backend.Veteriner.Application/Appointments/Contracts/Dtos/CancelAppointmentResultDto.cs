namespace Backend.Veteriner.Application.Appointments.Contracts.Dtos;

/// <summary>İptal yanıtı: istemci "Geri al"ı bu süre kadar gösterir (APPOINTMENT_CANCEL_UNDO_API_CONTRACT.md).</summary>
public sealed record CancelAppointmentResultDto(int UndoWindowSeconds);
