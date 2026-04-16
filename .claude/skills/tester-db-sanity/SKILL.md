---
name: tester-db-sanity
description: MSSQL'e bağlanıp data sanity sorguları yürütür — duplicate kontrol, foreign key orphan, NULL required columns, outlier değerler, row count eşikleri. Tester agent feature "done" kabulünden önce veritabanı tutarlılığını doğrular.
---

# tester-db-sanity

DB'de unit test koşuyor diye data doğru demek değil. Tester bu sorguları yürütür.

## Önkoşul

- MSSQL dev connection string: `.env` veya `appsettings.Development.json` (user-secrets).
- `sqlcmd` installed veya dotnet console app ile query çalıştır.

## Temel Sorgu Kümesi

### 1. Duplicate Kontrolü

```sql
-- Her aggregate'in unique constraint'ine göre duplicate tara
SELECT Symbol, OpenTime, COUNT(*) AS Duplicates
FROM Klines
GROUP BY Symbol, OpenTime
HAVING COUNT(*) > 1;
```

Beklenen: 0 row. Varsa 🚫 blocker — unique constraint eksik veya handler idempotent değil.

### 2. Foreign Key Orphan

```sql
-- Child varken parent yok mu?
SELECT o.*
FROM OrderItems o
LEFT JOIN Orders p ON o.OrderId = p.Id
WHERE p.Id IS NULL;
```

Beklenen: 0 row. Varsa ⚠️ — cascade delete problemi.

### 3. NULL in Required Column

```sql
SELECT Id FROM Klines WHERE Symbol IS NULL OR OpenTime IS NULL OR Close IS NULL;
```

Beklenen: 0 row. Varsa 🚫 blocker — EF configuration `.IsRequired()` uygulanmamış.

### 4. Outlier Değerler

```sql
-- Price 0 veya negatif?
SELECT TOP 10 * FROM Klines WHERE Close <= 0 OR Open <= 0;
-- Volume negatif?
SELECT TOP 10 * FROM Klines WHERE Volume < 0;
-- OpenTime gelecekten?
SELECT TOP 10 * FROM Klines WHERE OpenTime > DATEDIFF(ms, '1970-01-01', GETUTCDATE());
```

Beklenen: 0 row. Varsa ⚠️ — validator eksik veya stream data bozuk.

### 5. Row Count Sanity

```sql
-- Son saatte veri geldi mi?
SELECT COUNT(*) FROM Klines WHERE CreatedUtc >= DATEADD(hour, -1, GETUTCDATE());
```

Beklenen: > 0 (eğer WS stream aktifse). 0 ise ⚠️ — WS ingestion düşmüş olabilir.

## Connection — Örnek Bash + sqlcmd

```bash
# Local dev
sqlcmd -S localhost\SQLEXPRESS -E -d BinanceBotDb -Q "SELECT COUNT(*) FROM Klines;"

# Connection string ile
sqlcmd -S localhost -U sa -P "$DB_PASS" -d BinanceBotDb -Q "..."
```

Alternatif: küçük bir .NET console app (veya `dotnet run --project tools/db-check`) — sorgu koleksiyonu kod'da, test agent bunu çağırır.

## Kural

- **Read-only sorgular** — DELETE/UPDATE/INSERT tester'ın işi değil.
- Prod DB'ye bağlanma — dev/staging only.
- Sorgu sonuçlarını `.ai-trace/db-sanity-<date>.txt`'e yaz (committed değil — ephemeral).
- 🚫 blocker → PM'e bildir, reviewer "ready" vermeye kadar beklesin.

## Çıktı Formatı

```
🗄️ DB Sanity — <YYYY-MM-DD HH:MM UTC>

Duplicate check (Klines):       ✅ 0 row
FK orphan (OrderItems):          ✅ 0 row
NULL required (Klines):          ✅ 0 row
Outlier Close<=0 (Klines):       ⚠️ 3 row → ids: 42, 78, 103
Row count son 1h (Klines):       ✅ 1,247 row

Verdict: ⚠️ 3 outlier — validator eksik, backend-dev'e bildirildi.
```

## Kaynak

- https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-utility
