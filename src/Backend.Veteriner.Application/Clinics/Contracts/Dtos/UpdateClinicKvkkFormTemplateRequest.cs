namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos;

/// <param name="Text">Ham şablon metni (yer tutucular dahil).</param>
/// <param name="RowVersion">GET yanıtındaki sürüm; kayıt yokken (<c>isDefault=true</c>) null/boş gönderilir.</param>
public sealed record UpdateClinicKvkkFormTemplateRequest(string? Text, string? RowVersion);
