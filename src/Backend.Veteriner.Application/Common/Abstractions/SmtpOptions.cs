namespace Backend.Veteriner.Application.Common.Abstractions;

public sealed class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 25;
    public bool EnableSsl { get; set; } = false;   // 465 için
    public bool UseStartTls { get; set; } = false; // 587 için
    public string? User { get; set; }
    public string? Pass { get; set; }              // DİKKAT: "Pass" adı appsettings ile aynı olmalı
    public string From { get; set; } = "";
}