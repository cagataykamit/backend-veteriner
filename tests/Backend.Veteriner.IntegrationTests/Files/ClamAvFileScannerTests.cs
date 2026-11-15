using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Files;
using AwesomeAssertions;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Files;

/// <summary>
/// clamd INSTREAM istemcisi: sahte clamd sunucusuyla protokol (komut, parça çerçevesi, bitiş), yanıt çözümü, ulaşılamama,
/// süre aşımı ve boyut sınırı; gerçek clamd ile EICAR testi (127.0.0.1:3310 ya da VETINITY_CLAMD_HOST/PORT; yoksa atlanır).
/// </summary>
public sealed class ClamAvFileScannerTests
{
    private static ClamAvFileScanner Scanner(int port, string host = "127.0.0.1", int timeoutSeconds = 5, int maxFileSizeMb = 25)
        => new(Options.Create(new FileScanningOptions
        {
            Enabled = true,
            Clamd = new ClamdOptions { Host = host, Port = port, TimeoutSeconds = timeoutSeconds, MaxFileSizeMb = maxFileSizeMb },
        }));

    [Fact]
    public async Task Clean_Reply_Should_Pass_And_Content_Should_Arrive_Framed()
    {
        // Parça boyutunu aşan içerik birden çok parça olarak gider; sunucu çerçeveyi çözüp aynı baytları almalı.
        var content = Enumerable.Range(0, 150_000).Select(i => (byte)(i % 251)).ToArray();
        await using var clamd = new FakeClamd(_ => "stream: OK");

        var result = await Scanner(clamd.Port).ScanAsync(new MemoryStream(content), CancellationToken.None);

        result.Should().Be(FileScanResult.Clean);
        clamd.Commands.Should().Equal("zINSTREAM");
        clamd.Received.Should().Equal(content);
        clamd.ChunkCount.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task Found_Reply_Should_Return_Signature()
    {
        await using var clamd = new FakeClamd(_ => "stream: Win.Test.EICAR_HDB-1 FOUND");

        var result = await Scanner(clamd.Port).ScanAsync(new MemoryStream([1, 2, 3]), CancellationToken.None);

        result.Status.Should().Be(FileScanStatus.Infected);
        result.Signature.Should().Be("Win.Test.EICAR_HDB-1");
    }

    [Theory]
    [InlineData("INSTREAM size limit exceeded. ERROR")]
    [InlineData("stream: Can't allocate memory ERROR")]
    [InlineData("")]
    public async Task Error_Or_Unknown_Reply_Should_Fail(string reply)
    {
        await using var clamd = new FakeClamd(_ => reply);

        var result = await Scanner(clamd.Port).ScanAsync(new MemoryStream([1]), CancellationToken.None);

        result.Status.Should().Be(FileScanStatus.Failed);
        result.FailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Unreachable_Clamd_Should_Fail_Without_Exception()
    {
        var result = await Scanner(ClosedPort()).ScanAsync(new MemoryStream([1]), CancellationToken.None);

        result.Status.Should().Be(FileScanStatus.Failed);
        result.FailureReason.Should().Contain("unreachable");
    }

    [Fact]
    public async Task Silent_Clamd_Should_Fail_After_Timeout()
    {
        await using var clamd = new FakeClamd(_ => null);

        var result = await Scanner(clamd.Port, timeoutSeconds: 1).ScanAsync(new MemoryStream([1]), CancellationToken.None);

        result.Status.Should().Be(FileScanStatus.Failed);
        result.FailureReason.Should().Contain("did not answer");
    }

    [Fact]
    public async Task Content_Over_Scan_Limit_Should_Fail_Without_Sending()
    {
        await using var clamd = new FakeClamd(_ => "stream: OK");

        var result = await Scanner(clamd.Port, maxFileSizeMb: 1)
            .ScanAsync(new MemoryStream(new byte[1024 * 1024 + 1]), CancellationToken.None);

        result.Status.Should().Be(FileScanStatus.Failed);
        clamd.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task Request_Cancellation_Should_Propagate()
    {
        await using var clamd = new FakeClamd(_ => null);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var act = () => Scanner(clamd.Port).ScanAsync(new MemoryStream([1]), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Ping_Should_Report_Pong_And_Unreachable()
    {
        await using var clamd = new FakeClamd(_ => "PONG");

        (await Scanner(clamd.Port).PingAsync(CancellationToken.None)).Should().BeNull();
        clamd.Commands.Should().Equal("zPING");
        (await Scanner(ClosedPort()).PingAsync(CancellationToken.None)).Should().Contain("unreachable");
    }

    [Fact]
    public async Task Real_Clamd_Should_Detect_Eicar_And_Pass_Clean_Content()
    {
        var host = Environment.GetEnvironmentVariable("VETINITY_CLAMD_HOST") ?? "127.0.0.1";
        var port = int.TryParse(Environment.GetEnvironmentVariable("VETINITY_CLAMD_PORT"), out var p) ? p : 3310;
        var scanner = Scanner(port, host, timeoutSeconds: 30);
        var unreachable = await scanner.PingAsync(CancellationToken.None);
        Assert.SkipWhen(unreachable is not null, $"Gerçek clamd yok ({host}:{port}): {unreachable}");

        var infected = await scanner.ScanAsync(new MemoryStream(Encoding.ASCII.GetBytes(Eicar())), CancellationToken.None);
        var clean = await scanner.ScanAsync(new MemoryStream("Vetinity temiz dosya"u8.ToArray()), CancellationToken.None);

        infected.Status.Should().Be(FileScanStatus.Infected);
        infected.Signature!.ToUpperInvariant().Should().Contain("EICAR");
        clean.Should().Be(FileScanResult.Clean);
    }

    /// <summary>
    /// EICAR test dizesi çalışma anında birleştirilir: kaynakta ve derlenmiş dosyada bitişik durmaz, makinedeki virüs koruması test
    /// dosyalarını karantinaya almaz.
    /// </summary>
    private static string Eicar()
    {
        string[] parts = [@"X5O!P%@AP[4\PZX54(P^)7CC)7}$", "EICAR-STANDARD-", "ANTIVIRUS-TEST-FILE!$H+H*"];
        return string.Concat(parts);
    }

    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Sahte clamd: <c>z</c> önekli komutu okur, INSTREAM ise parçaları çözer ve verilen yanıtı <c>\0</c> ile yollar; yanıt
    /// <c>null</c> ise bağlantıyı açık tutup susar.
    /// </summary>
    private sealed class FakeClamd : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Func<string, string?> _reply;
        private readonly MemoryStream _received = new();
        private readonly Task _loop;

        public FakeClamd(Func<string, string?> reply)
        {
            _reply = reply;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = Task.Run(AcceptLoopAsync);
        }

        public int Port { get; }

        public List<string> Commands { get; } = [];

        public byte[] Received => _received.ToArray();

        public int ChunkCount { get; private set; }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await using var stream = client.GetStream();
                    await HandleAsync(stream, _stop.Token);
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
                // İstemci süre aşımıyla bağlantıyı kapattı.
            }
        }

        private async Task HandleAsync(NetworkStream stream, CancellationToken ct)
        {
            var command = await ReadCommandAsync(stream, ct);
            Commands.Add(command);

            if (command == "zINSTREAM")
            {
                var header = new byte[4];
                while (true)
                {
                    await stream.ReadExactlyAsync(header, ct);
                    var length = (int)BinaryPrimitives.ReadUInt32BigEndian(header);
                    if (length == 0)
                        break;
                    var chunk = new byte[length];
                    await stream.ReadExactlyAsync(chunk, ct);
                    _received.Write(chunk);
                    ChunkCount++;
                }
            }

            var reply = _reply(command);
            if (reply is null)
            {
                await Task.Delay(Timeout.Infinite, ct);
                return;
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes(reply + "\0"), ct);
        }

        private static async Task<string> ReadCommandAsync(NetworkStream stream, CancellationToken ct)
        {
            var text = new StringBuilder();
            var one = new byte[1];
            while (await stream.ReadAsync(one, ct) == 1 && one[0] != 0)
                text.Append((char)one[0]);
            return text.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try
            {
                await _loop;
            }
            catch (Exception)
            {
                // Kapatma sırasında yarım kalan bağlantı önemsiz.
            }

            _stop.Dispose();
        }
    }
}
