using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Integration test veritabanı adlarının tek kuralı: <c>&lt;taban ad&gt;_&lt;koşu kimliği&gt;</c>.
/// Her test koşusu (süreç) kendi veritabanlarını kullanır; böylece eşzamanlı koşular (birden fazla ajan,
/// IDE + CLI) aynı LocalDB adı/dosyası üzerinde çakışmaz.
///
/// Koşu kimliği: <c>INTEGRATION_TEST_RUN_ID</c> ortam değişkeni (harf/rakam; ör. <c>Run42</c>),
/// verilmezse süreç kimliğinden türetilen <c>p&lt;pid&gt;</c>. Taban adlar <see cref="IntegrationTestDatabaseGuard"/>
/// allowlist'ine uyar; ekli ad dev veritabanı adlarıyla (<c>VetinityCommandDb</c>, <c>VetinityQueryDb</c>) hiç çakışmaz.
///
/// Temizlik (<see cref="CleanUp"/>, koşu sonunda <see cref="IntegrationTestDatabaseCleanupFixture"/> çağırır): bu koşunun
/// veritabanları (ve .mdf/.ldf dosyaları) DROP edilir; ayrıca sahibi artık çalışmayan <c>p&lt;pid&gt;</c> artıkları
/// süpürülür. Açık kimlikli (<c>INTEGRATION_TEST_RUN_ID</c>) koşular yalnız kendi kimliğini temizler,
/// başkasının veritabanına dokunmaz.
/// </summary>
internal static class IntegrationTestDatabaseNames
{
    public const string RunIdEnvironmentVariable = "INTEGRATION_TEST_RUN_ID";

    private const string LocalDbServer = "(localdb)\\mssqllocaldb";
    private const int MaxRunIdLength = 24;

    private static readonly Regex TestDatabaseName =
        new("^Vetinity(Command|Query)Db_.+_(?<run>[A-Za-z0-9]+)$", RegexOptions.Compiled);

    private static readonly Regex AutoRunId = new("^p(?<pid>\\d+)$", RegexOptions.Compiled);

    public static string RunId { get; } = ResolveRunId();

    public static string Command(string scope) => WithRunId($"VetinityCommandDb_{scope}");

    public static string Query(string scope) => WithRunId($"VetinityQueryDb_{scope}");

    /// <summary>Verilen taban ada koşu kimliğini ekler.</summary>
    public static string WithRunId(string name) => $"{name}_{RunId}";

    /// <summary>LocalDB bağlantısı. 60 sn bağlantı süresi, CI'da LocalDB örneğinin geç uyanmasından doğan geçici bağlantı hatalarına karşıdır.</summary>
    public static string ConnectionString(string databaseName)
        => $"Server={LocalDbServer};Database={databaseName};Trusted_Connection=True;Connect Timeout=60;MultipleActiveResultSets=true";

    /// <summary>En iyi çaba: sessizce geçer, test sonucunu etkilemez.</summary>
    public static void CleanUp()
    {
        try
        {
            var master = new SqlConnectionStringBuilder(ConnectionString("master")) { ConnectTimeout = 10 }.ConnectionString;
            foreach (var name in ListOwnedOrStaleDatabases(master))
                IntegrationTestDatabaseReset.EnsureDroppedAsync(ConnectionString(name)).GetAwaiter().GetResult();
        }
        catch
        {
            // LocalDB erişilemiyorsa temizlenecek bir şey de oluşmamıştır.
        }
    }

    private static string ResolveRunId()
    {
        var configured = Environment.GetEnvironmentVariable(RunIdEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var sanitized = new string(configured.Where(char.IsAsciiLetterOrDigit).ToArray());
            if (sanitized.Length > 0)
                return sanitized.Length <= MaxRunIdLength ? sanitized : sanitized[..MaxRunIdLength];
        }

        return $"p{Environment.ProcessId}";
    }

    private static List<string> ListOwnedOrStaleDatabases(string masterConnectionString)
    {
        var names = new List<string>();
        using var connection = new SqlConnection(masterConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sys.databases WHERE name LIKE 'Vetinity%Db[_]%'";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            var match = TestDatabaseName.Match(name);
            if (!match.Success)
                continue;

            var run = match.Groups["run"].Value;
            if (string.Equals(run, RunId, StringComparison.Ordinal) || IsStaleAutoRun(run))
                names.Add(name);
        }

        return names;
    }

    private static bool IsStaleAutoRun(string run)
    {
        var match = AutoRunId.Match(run);
        if (!match.Success || !int.TryParse(match.Groups["pid"].Value, out var pid))
            return false;

        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}