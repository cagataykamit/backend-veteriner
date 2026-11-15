using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Backend.Veteriner.Application.Common.Abstractions;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Files;

/// <summary>
/// ClamAV tarayıcısı: clamd'ye TCP ile bağlanır ve içeriği INSTREAM komutuyla parça parça gönderir (her parça 4 bayt ağ sıralı
/// uzunluk + veri, sonda sıfır uzunluk). Protokol küçük olduğu için kütüphane kullanılmadı. Yanıtlar: <c>stream: OK</c> temiz,
/// <c>stream: &lt;imza&gt; FOUND</c> virüslü, diğerleri (ör. <c>INSTREAM size limit exceeded. ERROR</c>) tarama hatası.
/// Bağlantı, süre aşımı ve protokol hataları istisna değil <see cref="FileScanStatus.Failed"/> döner; yalnız isteğin kendi
/// iptali istisna olarak geçer.
/// </summary>
public sealed class ClamAvFileScanner : IFileMalwareScanner
{
    private const int ChunkBytes = 64 * 1024;
    private const int MaxReplyBytes = 4 * 1024;
    private const string ReplyPrefix = "stream: ";
    private const string InfectedSuffix = " FOUND";

    private readonly ClamdOptions _options;

    public ClamAvFileScanner(IOptions<FileScanningOptions> options) => _options = options.Value.Clamd;

    public async Task<FileScanResult> ScanAsync(Stream content, CancellationToken ct)
    {
        if (content.CanSeek && content.Length - content.Position > _options.MaxFileBytes)
            return FileScanResult.Failed($"Content exceeds the scan limit of {_options.MaxFileSizeMb} MB.");

        try
        {
            var reply = await ExchangeAsync("zINSTREAM\0", (stream, token) => SendChunksAsync(content, stream, token), ct);
            return ParseScanReply(reply);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return FileScanResult.Failed(Describe(ex));
        }
    }

    /// <summary>Sağlık kontrolü: clamd <c>PING</c> komutuna <c>PONG</c> yanıtı veriyorsa <c>null</c>, değilse neden.</summary>
    public async Task<string?> PingAsync(CancellationToken ct)
    {
        try
        {
            var reply = await ExchangeAsync("zPING\0", body: null, ct);
            return reply == "PONG" ? null : $"Unexpected clamd reply: {reply}";
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return Describe(ex);
        }
    }

    /// <summary>clamd INSTREAM yanıtını sonuca çevirir (sondaki <c>\0</c> ve boşluklar atılmış olarak).</summary>
    public static FileScanResult ParseScanReply(string reply)
    {
        if (reply == ReplyPrefix + "OK")
            return FileScanResult.Clean;

        if (reply.StartsWith(ReplyPrefix, StringComparison.Ordinal) && reply.EndsWith(InfectedSuffix, StringComparison.Ordinal))
            return FileScanResult.Infected(reply[ReplyPrefix.Length..^InfectedSuffix.Length]);

        return FileScanResult.Failed($"Unexpected clamd reply: {reply}");
    }

    private async Task<string> ExchangeAsync(string command, Func<Stream, CancellationToken, Task>? body, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        using var client = new TcpClient();
        await client.ConnectAsync(_options.Host, _options.Port, timeout.Token);
        await using var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes(command), timeout.Token);
        if (body is not null)
            await body(stream, timeout.Token);
        await stream.FlushAsync(timeout.Token);

        return await ReadReplyAsync(stream, timeout.Token);
    }

    private async Task SendChunksAsync(Stream content, Stream clamd, CancellationToken ct)
    {
        var buffer = new byte[4 + ChunkBytes];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(buffer.AsMemory(4, ChunkBytes), ct)) > 0)
        {
            total += read;
            if (total > _options.MaxFileBytes)
                throw new InvalidDataException($"Content exceeds the scan limit of {_options.MaxFileSizeMb} MB.");

            BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)read);
            await clamd.WriteAsync(buffer.AsMemory(0, 4 + read), ct);
        }

        await clamd.WriteAsync(new byte[4], ct);
    }

    private static async Task<string> ReadReplyAsync(Stream clamd, CancellationToken ct)
    {
        var reply = new MemoryStream();
        var buffer = new byte[256];
        int read;
        while ((read = await clamd.ReadAsync(buffer, ct)) > 0)
        {
            var end = Array.IndexOf(buffer, (byte)0, 0, read);
            reply.Write(buffer, 0, end >= 0 ? end : read);
            if (end >= 0 || reply.Length > MaxReplyBytes)
                break;
        }

        return Encoding.ASCII.GetString(reply.GetBuffer(), 0, (int)reply.Length).Trim();
    }

    private string Describe(Exception ex) => ex switch
    {
        OperationCanceledException => $"clamd did not answer within {_options.TimeoutSeconds} s ({_options.Host}:{_options.Port}).",
        SocketException socket => $"clamd unreachable at {_options.Host}:{_options.Port} ({socket.SocketErrorCode}).",
        _ => $"{ex.GetType().Name}: {ex.Message}",
    };
}
