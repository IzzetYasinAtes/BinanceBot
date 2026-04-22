$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Data
$conn = New-Object System.Data.SqlClient.SqlConnection 'Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;'
$conn.Open()

function Run-Q($label, $sql) {
    Write-Host "=== $label ==="
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    $a = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
    $dt = New-Object System.Data.DataTable
    [void]$a.Fill($dt)
    $dt | Format-Table -AutoSize | Out-String | Write-Host
}

Run-Q 'STRATEGY_SIGNALS_TOTAL' 'SELECT COUNT(*) AS N, MIN(EmittedAt) AS First, MAX(EmittedAt) AS Last FROM StrategySignals'
Run-Q 'SIGNALS_LAST_24H' "SELECT COUNT(*) AS N, MIN(EmittedAt) AS FirstIn24h, MAX(EmittedAt) AS LastIn24h FROM StrategySignals WHERE EmittedAt >= DATEADD(hour,-24,SYSUTCDATETIME())"
Run-Q 'SIGNALS_BY_SYMBOL_24H' "SELECT Symbol, COUNT(*) AS N, MAX(EmittedAt) AS Last FROM StrategySignals WHERE EmittedAt >= DATEADD(hour,-24,SYSUTCDATETIME()) GROUP BY Symbol ORDER BY N DESC"
Run-Q 'SIGNALS_LAST_10' 'SELECT TOP 10 Id, Symbol, Direction, EmittedAt FROM StrategySignals ORDER BY EmittedAt DESC'
Run-Q 'SYSTEM_EVENTS_LAST_30' 'SELECT TOP 30 Id, EventType, Severity, Source, OccurredAt FROM SystemEvents ORDER BY OccurredAt DESC'
Run-Q 'SYSTEM_EVENTS_SEVERITY_24H' "SELECT Severity, EventType, COUNT(*) AS N FROM SystemEvents WHERE OccurredAt >= DATEADD(hour,-24,SYSUTCDATETIME()) GROUP BY Severity, EventType ORDER BY N DESC"
Run-Q 'KLINES_FRESH' "SELECT Symbol, Interval, MAX(OpenTime) AS LastBar, COUNT(*) AS Bars FROM Klines GROUP BY Symbol, Interval ORDER BY Symbol, Interval"
Run-Q 'STRATEGIES_STATE' 'SELECT Id, Name, Type, Status, SymbolsCsv, UpdatedAt FROM Strategies'
Run-Q 'RISK_PROFILE_COLS' "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='RiskProfile' ORDER BY ORDINAL_POSITION"
Run-Q 'OPEN_POS_AGE' "SELECT Id, Symbol, OpenedAt, DATEDIFF(MINUTE, OpenedAt, SYSUTCDATETIME()) AS AgeMinutes FROM Positions WHERE Status=1"

$conn.Close()
Write-Host 'DONE'
