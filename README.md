# DumpSQL

Console application .NET/C# per estrarre dati da un database MSSQL o MySQL e generare file `.sql` pronti all'uso per il trasferimento su ambienti di debug o staging.

---

## Funzionamento

Per ogni tabella configurata, DumpSQL esegue:

```sql
SELECT * FROM <tabella> WHERE <campo_riferimento> > <valore_riferimento>
```

e genera un file `<tabella>_<data>.sql` con istruzioni **upsert**:

| Database | Sintassi generata |
|----------|-------------------|
| MSSQL    | `MERGE INTO … WHEN MATCHED … WHEN NOT MATCHED` |
| MySQL    | `INSERT INTO … ON DUPLICATE KEY UPDATE` |

---

## Requisiti

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Accesso a un server MSSQL o MySQL

---

## Installazione

```bash
git clone <url-repository>
cd DumpSQL
dotnet restore src/DumpSQL/DumpSQL.csproj
```

---

## Configurazione

Modifica `src/DumpSQL/appsettings.json` prima di eseguire l'applicazione:

```json
{
  "DatabaseType": "mssql",
  "ConnectionString": "Server=localhost;Database=MyDatabase;User Id=sa;Password=YourPassword;TrustServerCertificate=True;",
  "OutputDirectory": "output",
  "Tables": [
    {
      "TableName": "Customers",
      "ReferenceField": "UpdatedAt",
      "ReferenceValue": "2024-01-01T00:00:00"
    },
    {
      "TableName": "Orders",
      "ReferenceField": "OrderDate",
      "ReferenceValue": "2024-01-01T00:00:00"
    }
  ]
}
```

### Parametri

| Parametro | Valori | Descrizione |
|-----------|--------|-------------|
| `DatabaseType` | `mssql` · `mysql` | Tipo di server database |
| `ConnectionString` | stringa ADO.NET | Stringa di connessione al database |
| `OutputDirectory` | percorso relativo | Cartella dove vengono salvati i file `.sql` generati (default: `output`) |
| `Tables[].TableName` | nome tabella | Nome della tabella da esportare |
| `Tables[].ReferenceField` | nome colonna | Colonna usata nella clausola `WHERE` |
| `Tables[].ReferenceValue` | valore | Soglia: vengono estratte le righe con `ReferenceField > ReferenceValue` |

### Esempi di ConnectionString

**MSSQL – autenticazione SQL:**
```
Server=localhost;Database=MyDb;User Id=sa;Password=secret;TrustServerCertificate=True;
```

**MSSQL – autenticazione Windows:**
```
Server=localhost;Database=MyDb;Integrated Security=True;TrustServerCertificate=True;
```

**MySQL:**
```
Server=localhost;Port=3306;Database=MyDb;Uid=root;Pwd=secret;
```

---

## Utilizzo

```bash
# Esecuzione diretta
dotnet run --project src/DumpSQL/DumpSQL.csproj

# Build + eseguibile
dotnet publish src/DumpSQL/DumpSQL.csproj -c Release -o ./publish
./publish/DumpSQL.exe
```

### Output

I file vengono creati nella cartella `OutputDirectory` con il formato:

```
output/
├── Customers_20250422.sql
└── Orders_20250422.sql
```

Esempio di contenuto (MSSQL):

```sql
-- Dump generato il 2025-04-22 10:30:00 -- Tabella: Customers

MERGE INTO [Customers] AS target
USING (VALUES ('C001', 'Mario Rossi', '2025-01-15 08:00:00')) AS source ([Id], [Name], [UpdatedAt])
ON target.[Id] = source.[Id]
WHEN MATCHED THEN
    UPDATE SET target.[Name] = source.[Name], target.[UpdatedAt] = source.[UpdatedAt]
WHEN NOT MATCHED THEN
    INSERT ([Id], [Name], [UpdatedAt]) VALUES ('C001', 'Mario Rossi', '2025-01-15 08:00:00');
```

---

## Log

I log vengono scritti nella cartella `log/` con un file per giorno:

```
log/
└── 20250422.log
```

All'avvio, i file di log più vecchi di **30 giorni** vengono eliminati automaticamente.

---

## Struttura del progetto

```
DumpSQL/
├── src/
│   └── DumpSQL/
│       ├── Program.cs
│       ├── appsettings.json
│       ├── Models/
│       │   ├── AppSettings.cs
│       │   └── TableConfig.cs
│       └── Services/
│           ├── IDatabaseService.cs
│           ├── MssqlDatabaseService.cs
│           ├── MysqlDatabaseService.cs
│           ├── SqlFileGenerator.cs
│           └── LogCleanupService.cs
└── .github/
    └── copilot-instructions.md
```

---

## Dipendenze NuGet

| Pacchetto | Versione | Scopo |
|-----------|----------|-------|
| `Microsoft.Data.SqlClient` | 7.0.0 | Connessione MSSQL |
| `MySqlConnector` | 2.5.0 | Connessione MySQL |
| `Serilog` + sinks | 4.3.1 | Logging su console e file |
| `Microsoft.Extensions.Configuration` | 10.0.7 | Lettura `appsettings.json` |
| `Microsoft.Extensions.DependencyInjection` | 10.0.7 | Dependency injection |
