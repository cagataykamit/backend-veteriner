namespace Backend.Veteriner.Application.Examinations.Contracts.Dtos;

/// <summary>Başarılı muayene güncellemesi sonrası kayıt kimliği ve güncel sürüm.</summary>
public sealed record ExaminationWriteResultDto(Guid Id, string RowVersion);
