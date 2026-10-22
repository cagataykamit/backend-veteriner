using System.Text.RegularExpressions;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Files;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Files;

/// <summary>
/// Yerel disk deposu (ASAMA6 D4; v1). Kök <c>FileStorage:RootPath</c>; anahtar yalnız sunucu biçiminde kabul edilir
/// (onaltılık parçalar ve <c>/</c>), kök altında çözülür ve kök dışına çıkan yol reddedilir. Yazma yalnız yeni dosya oluşturur
/// (<see cref="FileMode.CreateNew"/>: var olan içerik ezilmez); yarıda kalan yazma silinir. Dosyalar statik sunulmaz.
/// </summary>
public sealed partial class LocalDiskFileStorage : IFileStorage
{
    private const int BufferSize = 81920;

    private readonly FileStorageOptions _options;

    public LocalDiskFileStorage(IOptions<FileStorageOptions> options) => _options = options.Value;

    public async Task WriteAsync(string storageKey, Stream content, CancellationToken ct)
    {
        var path = ResolvePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // CreateNew var olan dosyada hata verir; o durumda dosya bu çağrının değildir ve silinmez.
        var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
        var completed = false;
        try
        {
            await using (target)
            {
                await content.CopyToAsync(target, BufferSize, ct);
                await target.FlushAsync(ct);
            }

            completed = true;
        }
        finally
        {
            if (!completed)
                TryDelete(path);
        }
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct)
    {
        var path = ResolvePath(storageKey);
        if (!File.Exists(path))
            return Task.FromResult<Stream?>(null);

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        var path = ResolvePath(storageKey);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    /// <summary>Anahtarı kök altında mutlak yola çevirir; biçim dışı anahtar veya kök dışı yol hatadır.</summary>
    internal string ResolvePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || !StorageKeyPattern().IsMatch(storageKey))
            throw new ArgumentException("Depolama anahtarı geçersiz.", nameof(storageKey));

        var root = ResolveRoot();
        var full = Path.GetFullPath(Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Depolama anahtarı kök dizin dışını gösteriyor.", nameof(storageKey));

        return full;
    }

    private string ResolveRoot()
    {
        if (string.IsNullOrWhiteSpace(_options.RootPath))
            throw new FileStorageUnavailableException("FileStorage:RootPath yapılandırılmamış.");

        var root = Path.GetFullPath(Path.IsPathRooted(_options.RootPath)
            ? _options.RootPath
            : Path.Combine(AppContext.BaseDirectory, _options.RootPath));
        return Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Bakım işi yarım yüklemeyi yeniden dener.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Sunucu anahtar biçimi: <c>{tenant}/{clinic}/{yyyy}/{MM}/{fileId}</c>, yalnız onaltılık parçalar.</summary>
    [GeneratedRegex("^[0-9a-f]{32}/[0-9a-f]{32}/[0-9]{4}/[0-9]{2}/[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex StorageKeyPattern();
}
