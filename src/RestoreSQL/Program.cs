using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using RestoreSQL.Models;
using RestoreSQL.Services;
using Serilog;
using Serilog.Debugging;

// ── Serilog self-diagnostics ────────────────────────────────────────────────
SelfLog.Enable(msg => Console.Error.WriteLine($"[SERILOG] {msg}"));

var logDirectory = "log";

#if DEBUG
foreach (var dir in new[] { logDirectory })
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
    Log.Information("RestoreSQL avviato");

    // ── Cleanup log vecchi ──────────────────────────────────────────────────
    CleanupOldLogs(logDirectory, days: 30);

    // ── Risoluzione file di configurazione ─────────────────────────────────
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

    // ── Ricerca file .sql ───────────────────────────────────────────────────
    var inputDir = Path.IsPathRooted(settings.InputDirectory)
        ? settings.InputDirectory
        : Path.Combine(configDir, settings.InputDirectory);

    if (!Directory.Exists(inputDir))
        throw new DirectoryNotFoundException($"InputDirectory non trovata: {inputDir}");

    var sqlFiles = Directory.GetFiles(inputDir, "*.sql")
                            .OrderBy(f => f)
                            .ToList();

    if (sqlFiles.Count == 0)
    {
        Log.Warning("Nessun file .sql trovato in: {Dir}", inputDir);
        return;
    }

    Log.Information("Database: {Type} | File: {Count} | Input: {Dir}",
        settings.DatabaseType, sqlFiles.Count, settings.InputDirectory);

    // ── Pipeline principale ─────────────────────────────────────────────────
    var restore  = new RestoreService(settings.ConnectionString, settings.DatabaseType);
    var totalSw  = Stopwatch.StartNew();
    int okFiles  = 0;
    int errFiles = 0;

    Log.Information("Disabilitazione foreign key constraints...");
    await restore.DisableConstraintsAsync(cts.Token);

    try
    {
    foreach (var file in sqlFiles)
    {
        if (cts.Token.IsCancellationRequested) break;

        var fileName = Path.GetFileName(file);
        Log.Information("Elaborazione: {File}", fileName);

        var fileSw = Stopwatch.StartNew();
        try
        {
            var (stmtOk, stmtSkipped, error) =
                await restore.ExecuteFileAsync(file, cts.Token);

            if (error is null)
            {
                Log.Information("OK: {File} - {Ok} statement in {Elapsed:F1}s",
                    fileName, stmtOk, fileSw.Elapsed.TotalSeconds);
                okFiles++;
            }
            else
            {
                Log.Error("ERRORE: {File} - {Ok}/{Total} statement ok - {Error}",
                    fileName, stmtOk, stmtOk + stmtSkipped, error);
                errFiles++;
            }
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Elaborazione {File} annullata.", fileName);
            break;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Errore imprevisto su {File}", fileName);
            errFiles++;
        }
    }
    }
    finally
    {
        Log.Information("Riabilitazione foreign key constraints...");
        try { await restore.EnableConstraintsAsync(); }
        catch (Exception ex) { Log.Warning(ex, "Impossibile riabilitare FK constraints"); }
    }

    Log.Information("RestoreSQL completato in {Elapsed:F1}s. OK: {Ok} | Errori: {Err}",
        totalSw.Elapsed.TotalSeconds, okFiles, errFiles);

    if (errFiles > 0)
        Environment.Exit(2);
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
    var idx = Array.FindIndex(args, a => a.Equals("--config", StringComparison.OrdinalIgnoreCase));
    if (idx >= 0 && idx + 1 < args.Length)
        return args[idx + 1];

    var positional = args.FirstOrDefault(a => !a.StartsWith('-'));
    if (positional is not null)
        return positional;

    return Path.Combine(AppContext.BaseDirectory, "appsettings.json");
}

static void CleanupOldLogs(string dir, int days)
{
    if (!Directory.Exists(dir)) return;
    var cutoff = DateTime.Now.AddDays(-days);
    foreach (var f in Directory.GetFiles(dir, "*.log"))
        if (File.GetLastWriteTime(f) < cutoff)
            try { File.Delete(f); } catch { /* ignora */ }
}
