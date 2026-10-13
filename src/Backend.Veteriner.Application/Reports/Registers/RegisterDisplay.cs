using System.Globalization;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Reports.Registers;

/// <summary>Defterlerde ortak kullanılan görüntü kuralları (tek yerde).</summary>
public static class RegisterDisplay
{
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string? Gender(PetGender? gender)
        => gender switch
        {
            PetGender.Male => "Erkek",
            PetGender.Female => "Dişi",
            _ => null,
        };

    /// <summary>Hayvanın <paramref name="on"/> tarihindeki yaşı: <c>N yıl M ay</c>; 1 yıldan küçükse <c>M ay</c>; 1 aydan küçükse <c>G gün</c>.</summary>
    public static string? Age(DateOnly? birthDate, DateOnly on)
    {
        if (birthDate is not { } birth || birth > on)
            return null;

        var months = (on.Year - birth.Year) * 12 + on.Month - birth.Month;
        if (on.Day < birth.Day)
            months--;

        if (months >= 12)
        {
            var years = months / 12;
            var rest = months % 12;
            return rest == 0 ? $"{years} yıl" : $"{years} yıl {rest} ay";
        }

        return months > 0 ? $"{months} ay" : $"{on.DayNumber - birth.DayNumber} gün";
    }

    /// <summary>Miktar: gereksiz sıfırsız, Türkçe ondalık.</summary>
    public static string Quantity(decimal quantity)
        => quantity.ToString("0.###", Turkish);
}
