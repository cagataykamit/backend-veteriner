namespace Backend.Veteriner.Application.Common.Abstractions;

public interface IAppUrlProvider
{
    // �rn: BuildAbsolute("/api/email/confirm", "token=....")
    string BuildAbsolute(string path, string? query = null);
}
