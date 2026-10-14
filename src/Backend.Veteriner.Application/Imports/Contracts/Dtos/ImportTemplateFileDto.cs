namespace Backend.Veteriner.Application.Imports.Contracts.Dtos;

public sealed record ImportTemplateFileDto(byte[] Content, string ContentType, string FileName);
