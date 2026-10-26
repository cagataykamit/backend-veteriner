using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Imports.Queries.GetTemplate;

/// <summary>GET /api/v1/imports/template: <c>xlsx</c> (üç sayfa) veya <c>csv</c> + tablo anahtarı.</summary>
public sealed record GetImportTemplateQuery(string? Format, string? Table) : IRequest<Result<ImportTemplateFileDto>>;
