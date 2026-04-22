# Copilot Instructions – DumpSQL

## Scopo del progetto

**DumpSQL** è una console application .NET/C# che si connette a un server MSSQL o MySQL, esegue query su tabelle configurate e genera file `.sql` con sintassi `MERGE` (MSSQL) o `INSERT … ON DUPLICATE KEY UPDATE` (MySQL) pronti per essere eseguiti su un ambiente di debug/staging.

---

## Stack tecnologico

- **.NET 8** (Console App, C#)
- **Microsoft.Data.SqlClient** – connessione MSSQL
- **MySqlConnector** – connessione MySQL (preferito a MySql.Data)
- **Serilog** + `Serilog.Sinks.File` – logging su file rolling giornaliero
- **Microsoft.Extensions.Configuration** + `Microsoft.Extensions.Configuration.Json` – lettura `appsettings.json`
- **Microsoft.Extensions.DependencyInjection** – dependency injection

---

## Architettura

```
DumpSQL/
├── src/
│   └── DumpSQL/
│       ├── Program.cs                  # Entry point: DI setup, caricamento config, avvio pipeline
│       ├── appsettings.json            # Configurazione principale (vedi sezione Config)
│       ├── Models/
│       │   ├── AppSettings.cs          # Root del modello di configurazione
│       │   └── TableConfig.cs          # Configurazione per ogni tabella
│       ├── Services/
│       │   ├── IDatabaseService.cs     # Interfaccia: connessione + query
│       │   ├── MssqlDatabaseService.cs # Implementazione MSSQL
│       │   ├── MysqlDatabaseService.cs # Implementazione MySQL
│       │   ├── SqlFileGenerator.cs     # Generazione file .sql di output
│       │   └── LogCleanupService.cs    # Eliminazione log > 1 mese all'avvio
│       └── log/                        # Cartella log (gitignored)
└── .github/
    └── copilot-instructions.md
```

### Flusso di esecuzione

1. `Program.cs` carica `appsettings.json`, registra i servizi e avvia il cleanup log
2. Per ogni tabella in `Tables[]`: viene costruita la query `SELECT * FROM <table> WHERE <ReferenceField> > <ReferenceValue>`
3. `IDatabaseService` esegue la query e restituisce i dati come `List<Dictionary<string, object>>`
4. `SqlFileGenerator` genera il file `<table>_<yyyyMMdd>.sql` nella directory configurata (default: `output/`)
5. Il file .sql usa `MERGE` per MSSQL e `INSERT … ON DUPLICATE KEY UPDATE` per MySQL

---

## Configurazione (`appsettings.json`)

```json
{
  "DatabaseType": "mssql",
  "ConnectionString": "Server=...;Database=...;User Id=...;Password=...;",
  "OutputDirectory": "output",
  "Tables": [
    {
      "TableName": "Customers",
      "ReferenceField": "UpdatedAt",
      "ReferenceValue": "2024-01-01T00:00:00"
    }
  ]
}
```

- `DatabaseType`: `"mssql"` oppure `"mysql"`
- `ReferenceField` / `ReferenceValue`: usati nella clausola `WHERE ReferenceField > ReferenceValue`
- Il servizio database corretto viene risolto via DI in base a `DatabaseType`

---

## Logging

- Serilog scrive in `log/<yyyyMMdd>.log`
- All'avvio, `LogCleanupService` elimina tutti i file nella cartella `log/` con data di creazione superiore a 30 giorni
- Livello minimo: `Information`; gli errori di connessione/query sono `Error`

---

## Convenzioni di codice

- Tutti i servizi implementano un'interfaccia (`IXxxService`)
- I servizi sono registrati come `Scoped` nel container DI
- I nomi di file di output sono sempre in formato: `<NomeTabella>_<yyyyMMdd>.sql` (es. `Customers_20250422.sql`)
- Il tipo del database viene risolto con una factory o switch in `Program.cs`, non via reflection
- Nessuna logica di business in `Program.cs`: solo composizione DI e orchestrazione
- I valori null nei risultati della query vanno serializzati come `NULL` nel file SQL generato
- Le stringhe nei valori SQL vanno escapate (sostituire `'` con `''`)

---

## Comandi principali

```bash
# Build
dotnet build src/DumpSQL/DumpSQL.csproj

# Run
dotnet run --project src/DumpSQL/DumpSQL.csproj

# Publish
dotnet publish src/DumpSQL/DumpSQL.csproj -c Release -o ./publish
```

---

## File da non versionare (`.gitignore` consigliato)

```
output/
log/
publish/
*.user
.vs/
```
