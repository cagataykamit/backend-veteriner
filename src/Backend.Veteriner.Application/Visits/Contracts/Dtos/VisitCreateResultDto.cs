namespace Backend.Veteriner.Application.Visits.Contracts.Dtos;

/// <summary>Geliş kaydı sonucu; <see cref="Created"/> false ise tekrar istek mevcut kaydı döndürmüştür.</summary>
public sealed record VisitCreateResultDto(bool Created, VisitDto Visit);
