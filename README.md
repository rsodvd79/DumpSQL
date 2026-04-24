# DumpSQL

<p align="center">
  <img src="assets/icon.svg" width="96" height="96" alt="DumpSQL icon"/>
</p>

Console application .NET 8 / C# per estrarre dati da un database MSSQL o MySQLe generare file `.sql` pronti all'uso con sintassi **upsert**, destinati al trasporto verso ambienti di debug o staging.

---

## Funzionamento

Per ogni tabella configurata, DumpSQL esegue:

```sql
SELECT * FROM <tabella> WHERE <campo_riferimento> > <valore_riferimento>
```

e genera un file `<tabella>_<data>.sql` in streaming con istruzioni **upsert**:

| Database | Sintassi generata |
|----------|-------------------|
| MSSQL    | `MERGE INTO … WHEN MATCHED … WHEN NOT MATCHED` |
| MySQL    | `INSERT INTO … VALUES (…),(…) ON DUPLICATE KEY UPDATE` (batch da 500 righe) |

La clausola `ON` del MERGE (o la chiave dell'upsert MySQL) viene costruita a partire dal campo `KeyColumns` di ciascuna tabella. Se `KeyColumns` è vuoto, viene usata la prima colonna del resultset come fallback.

---

## Download

I binari precompilati sono disponibili nella pagina [**Releases**](../../releases/latest) per tutte le piattaforme supportate:

| Pacchetto | Piattaforma |
|-----------|-------------|
| `DumpSQL-Suite-*-win-x64.zip`   | Windows 64-bit (Intel / AMD) |
| `DumpSQL-Suite-*-win-arm64.zip` | Windows ARM64 |
| `DumpSQL-Suite-*-osx-x64.zip`   | macOS Intel |
| `DumpSQL-Suite-*-osx-arm64.zip` | macOS Apple Silicon (M-series) |

Ogni pacchetto è **self-contained**: non richiede .NET installato sulla macchina.  
Estrarre lo zip, editare `appsettings.json` con la propria stringa di connessione ed eseguire il binario.

---

## Requisiti

**Per usare i binari precompilati:**
- Nessuna dipendenza — i pacchetti sono self-contained

**Per compilare dal sorgente:**
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

**In entrambi i casi:**
- Accesso a un server MSSQL (SQL Server) o MySQL

---

## Installazione

```bash
git clone <url-repository>
cd DumpSQL
dotnet restore src/DumpSQL/DumpSQL.csproj
```

---

## Configurazione

Il file di configurazione predefinito è `appsettings.json` nella directory di lavoro.  
È possibile fornire un file alternativo dalla riga di comando (vedi sezione [Utilizzo](#utilizzo)).

```json
{
  "DatabaseType": "mssql",
  "ConnectionString": "Server=localhost;Database=MyDatabase;User Id=sa;Password=YourPassword;TrustServerCertificate=True;",
  "OutputDirectory": "output",
  "Tables": [
    {
      "TableName": "dbo.Customers",
      "ReferenceField": "UpdatedAt",
      "ReferenceValue": "2024-01-01",
      "KeyColumns": [ "Id" ]
    },
    {
      "TableName": "dbo.OrderLines",
      "ReferenceField": "OrderDate",
      "ReferenceValue": "2024-01-01",
      "KeyColumns": [ "OrderId", "LineId" ]
    }
  ]
}
```

### Parametri

| Parametro | Valori | Descrizione |
|-----------|--------|-------------|
| `DatabaseType` | `mssql` · `mysql` | Tipo di server database |
| `ConnectionString` | stringa ADO.NET | Stringa di connessione al database |
| `OutputDirectory` | percorso relativo | Cartella dove vengono salvati i file `.sql` (default: `output`) |
| `Tables[].TableName` | `[schema.]tabella` | Nome della tabella (con schema opzionale, es. `dbo.Orders`) |
| `Tables[].ReferenceField` | nome colonna | Colonna usata nella clausola `WHERE` per il filtro incrementale |
| `Tables[].ReferenceValue` | valore | Soglia: vengono estratte le righe con `ReferenceField > ReferenceValue` |
| `Tables[].KeyColumns` | array di stringhe | Colonne chiave per la clausola `ON` del MERGE / upsert. Se omesso, usa la prima colonna. |

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
# File di configurazione predefinito (appsettings.json)
dotnet run --project src/DumpSQL/DumpSQL.csproj

# File di configurazione alternativo (flag --config)
dotnet run --project src/DumpSQL/DumpSQL.csproj -- --config appsettings-mysql.json

# Argomento posizionale (equivalente)
dotnet run --project src/DumpSQL/DumpSQL.csproj -- /percorso/config.json

# Build + eseguibile
dotnet publish src/DumpSQL/DumpSQL.csproj -c Release -o ./publish
./publish/DumpSQL.exe --config appsettings-mysql.json
```

### Interruzione

Premere **Ctrl+C** per interrompere il dump in modo pulito. I file parziali vengono eliminati automaticamente.

### Output

I file vengono creati nella cartella `OutputDirectory` con il formato `<tabella>_<yyyyMMdd>.sql`:

```
output/
├── dbo_Customers_20260422.sql
└── dbo_OrderLines_20260422.sql
```

Esempio di contenuto MSSQL (con schema e chiave composita):

```sql
-- Dump generato il 2026-04-22 10:30:00 -- Tabella: dbo.Customers

MERGE INTO [dbo].[Customers] AS target
USING (VALUES (1, 'Mario Rossi', '2026-01-15 08:00:00')) AS source ([Id], [Name], [UpdatedAt])
ON target.[Id] = source.[Id]
WHEN MATCHED THEN
    UPDATE SET target.[Name] = source.[Name], target.[UpdatedAt] = source.[UpdatedAt]
WHEN NOT MATCHED THEN
    INSERT ([Id], [Name], [UpdatedAt]) VALUES (1, 'Mario Rossi', '2026-01-15 08:00:00');
```

Esempio di contenuto MySQL (batch upsert):

```sql
-- Dump generato il 2026-04-22 10:30:00 -- Tabella: orders

INSERT INTO `orders` (`id`, `customer_id`, `total`)
VALUES (1, 42, 99.90),
       (2, 43, 150.00)
ON DUPLICATE KEY UPDATE `total` = VALUES(`total`);
```

---

## RestoreSQL

**RestoreSQL** è il complemento di DumpSQL: legge i file `.sql` generati e li applica su un database target tramite upsert.

### Utilizzo

```bash
# File di configurazione predefinito (appsettings.json)
dotnet run --project src/RestoreSQL/RestoreSQL.csproj

# File di configurazione alternativo
dotnet run --project src/RestoreSQL/RestoreSQL.csproj -- --config appsettings-mysql.json

# Build + eseguibile
dotnet publish src/RestoreSQL/RestoreSQL.csproj -c Release -o ./publish
./publish/RestoreSQL.exe --config appsettings-mysql.json
```

### Configurazione

```json
{
  "DatabaseType": "mssql",
  "ConnectionString": "Server=localhost;Database=MyDb;User Id=sa;Password=secret;TrustServerCertificate=True;",
  "InputDirectory": "input",
  "TableOrder": [
    "THIP_REPARTI",
    "THIP_FORNITORI",
    "THIP_ARTICOLI",
    "THIP_ART_VERSIONI",
    "THIP_ARTICOLI_CLI"
  ]
}
```

| Parametro | Descrizione |
|-----------|-------------|
| `DatabaseType` | `mssql` o `mysql` |
| `ConnectionString` | Stringa di connessione al database target |
| `InputDirectory` | Cartella contenente i file `.sql` da applicare (default: `input`) |
| `TableOrder` | **Ordine di elaborazione** delle tabelle (array di nomi senza suffisso data). I file il cui nome inizia con una voce dell'elenco vengono elaborati nell'ordine specificato; quelli non presenti vengono elaborati dopo, in ordine alfabetico. Usato per rispettare le dipendenze di Foreign Key. |

### Comportamento

- Esegue tutti i file `.sql` presenti in `InputDirectory` nell'ordine definito da `TableOrder`, poi i restanti in ordine alfabetico
- Ogni file viene applicato in una **transazione**: se un'istruzione fallisce, il file viene annullato (rollback) e si continua con il successivo
- I file generati da DumpSQL sono già in sintassi upsert (MERGE / INSERT ON DUPLICATE KEY UPDATE)
- In caso di errori, il processo esce con codice `2` (utile per script CI/CD)

---

## Log

I log vengono scritti su console e nella cartella `log/` con un file per giorno:

```
log/
└── 20260422.log
```

All'avvio, i file di log più vecchi di **30 giorni** vengono eliminati automaticamente.

> **Modalità Debug**: all'avvio in configurazione `Debug`, la cartella `log/` viene svuotata automaticamente. Per DumpSQL viene svuotata anche `output/`.

---

## Struttura del progetto

```
DumpSQL/
├── src/
│   ├── DumpSQL/
│   │   ├── Program.cs
│   │   ├── appsettings.json              # configurazione MSSQL (THIP, 14 tabelle)
│   │   ├── appsettings-mysql.json        # configurazione MySQL (matrixintranet, 9 tabelle)
│   │   ├── appsettings-biemmedb.json     # configurazione MySQL (biemmedb MES, 83 tabelle)
│   │   ├── Models/
│   │   │   ├── AppSettings.cs
│   │   │   └── TableConfig.cs
│   │   └── Services/
│   │       ├── IDatabaseService.cs
│   │       ├── MssqlDatabaseService.cs
│   │       ├── MysqlDatabaseService.cs
│   │       ├── SqlFileGenerator.cs
│   │       ├── SqlQuoting.cs
│   │       └── LogCleanupService.cs
│   └── RestoreSQL/
│       ├── Program.cs
│       ├── appsettings.json              # configurazione MSSQL target
│       ├── appsettings-mysql.json        # configurazione MySQL target (biemmedb)
│       ├── Models/
│       │   └── AppSettings.cs
│       └── Services/
│           └── RestoreService.cs
└── DumpSQL.slnx
```

---

## Dipendenze NuGet

| Pacchetto | Versione | Scopo |
|-----------|----------|-------|
| `Microsoft.Data.SqlClient` | 7.0.0 | Connessione MSSQL |
| `MySqlConnector` | 2.5.0 | Connessione MySQL |
| `Serilog` + sinks | 4.3.1 | Logging su console e file |
| `Microsoft.Extensions.Configuration` | 10.0.7 | Lettura `appsettings.json` |
