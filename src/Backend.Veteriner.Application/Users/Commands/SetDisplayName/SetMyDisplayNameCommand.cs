using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Users.Commands.SetDisplayName;

/// <summary>Oturum acmis kullanicinin kendi gorunen adini ayarlar; bos deger adi temizler (e-posta turevine doner).</summary>
public sealed record SetMyDisplayNameCommand(string? DisplayName) : IRequest<Result>;
