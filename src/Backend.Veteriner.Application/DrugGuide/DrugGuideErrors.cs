namespace Backend.Veteriner.Application.DrugGuide;

public static class DrugGuideErrors
{
    public const string ProductNotFoundCode = "DrugGuide.ProductNotFound";
    public const string ProductNotFoundMessage = "Ruhsatlı ürün kaydı bulunamadı.";
    public const string IngredientNotFoundCode = "DrugGuide.IngredientNotFound";
    public const string IngredientNotFoundMessage = "Etken madde bulunamadı.";

    /// <summary>Rehber aramasında en çok bu kadar kelime dikkate alınır.</summary>
    public const int MaxSearchTokens = 5;
}
