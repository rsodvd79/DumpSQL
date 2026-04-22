using System.Diagnostics;
using DumpSQL.Models;
using DumpSQL.Services;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Debugging;

// ── Serilog self-diagnostics ────────────────────────────────────────────────
SelfLog.Enable(msg => Console.Error.WriteLine($"[SERILOG] {msg}"));

var logDirectory = "log";

#if DEBUG
// Pulizia cartelle prima di inizializzare il logger (evita lock sul file)
foreach (var dir in new[] { logDirectory, "output" })
{
    if (Directory.Exists(dir))
        foreach (var f in Directory.GetFiles(dir))
            File.Delete(f);
}
#endif

Directory.CreateDirectory(logDirectory);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: Path.Combine(logDirectory, ".log"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

// ── Gestione Ctrl+C ─────────────────────────────────────────────────────────
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Log.Warning("Interruzione richiesta (Ctrl+C)...");
    cts.Cancel();
};

try
{
    Log.Information("DumpSQL avviato");

    // ── Cleanup log vecchi ──────────────────────────────────────────────────
    new LogCleanupService(logDirectory).Cleanup();

    // ── Risoluzione file di configurazione ─────────────────────────────────
    //    Uso: DumpSQL [--config <percorso>]  oppure  DumpSQL <percorso>
    var configFile = ResolveConfigFile(args);
    var configDir  = Path.GetDirectoryName(Path.GetFullPath(configFile))!;

    Log.Information("Configurazione: {File}", Path.GetFullPath(configFile));

    // ── Caricamento configurazione ──────────────────────────────────────────
    var configuration = new ConfigurationBuilder()
        .SetBasePath(configDir)
        .AddJsonFile(Path.GetFileName(configFile), optional: false, reloadOnChange: false)
        .Build();

    var settings = configuration.Get<AppSettings>()
        ?? throw new InvalidOperationException($"File di configurazione non valido: {configFile}");

    if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        throw new InvalidOperationException("ConnectionString non configurata.");

    if (settings.Tables.Count == 0)
    {
        Log.Warning("Nessuna tabella configurata. Uscita.");
        return;
    }

    // ── Selezione servizio database ─────────────────────────────────────────
    IDatabaseService dbService = settings.DatabaseType.ToLowerInvariant() switch
    {
        "mysql" => new MysqlDatabaseService(settings.ConnectionString),
        "mssql" => new MssqlDatabaseService(settings.ConnectionString),
        _ => throw new InvalidOperationException(
            $"DatabaseType non supportato: '{settings.DatabaseType}'. Valori validi: mssql, mysql")
    };

    var generator = new SqlFileGenerator(settings.OutputDirectory, settings.DatabaseType);

    // ── Pipeline principale ─────────────────────────────────────────────────
    Log.Information("Database: {Type} | Tabelle: {Count} | Output: {Dir}",
        settings.DatabaseType, settings.Tables.Count, settings.OutputDirectory);

    var totalSw = Stopwatch.StartNew();

    foreach (var table in settings.Tables)
    {
        if (cts.Token.IsCancellationRequested) break;

        Log.Information("Elaborazione: {Table} (WHERE {Field} > {Value})",
            table.TableName, table.ReferenceField, table.ReferenceValue);

        var tableSw = Stopwatch.StartNew();
        try
        {
            var rows = dbService.QueryTableAsync(
                table.TableName,
                table.ReferenceField,
                table.ReferenceValue,
                cts.Token);

            await generator.GenerateAsync(table.TableName, table.KeyColumns, rows, cts.Token);

            Log.Debug("{Table} completata in {Elapsed}ms", table.TableName, tableSw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Elaborazione {Table} annullata.", table.TableName);
            break;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Errore durante l'elaborazione della tabella {Table}", table.TableName);
        }
    }

    Log.Information("DumpSQL completato in {Elapsed:F1}s.", totalSw.Elapsed.TotalSeconds);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Errore fatale. Applicazione terminata.");
    Environment.Exit(1);
}
finally
{
    await Log.CloseAndFlushAsync();
}

// ── Helper ──────────────────────────────────────────────────────────────────
static string ResolveConfigFile(string[] args)
{
    // --config <path>
    var idx = Array.FindIndex(args, a => a.Equals("--config", StringComparison.OrdinalIgnoreCase));
    if (idx >= 0 && idx + 1 < args.Length)
        return args[idx + 1];

    // primo argomento posizionale (non flag)
    var positional = args.FirstOrDefault(a => !a.StartsWith('-'));
    if (positional is not null)
        return positional;

    // default
    return Path.Combine(AppContext.BaseDirectory, "appsettings.json");
}

