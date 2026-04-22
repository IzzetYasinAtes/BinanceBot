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

Run-Q 'POSITIONS_CLOSED' 'SELECT COUNT(*) AS N, CAST(SUM(RealizedPnl) AS decimal(18,6)) AS TotalRealized, MIN(ClosedAt) AS FirstClose, MAX(ClosedAt) AS LastClose FROM Positions WHERE Status=2'
Run-Q 'FILLS_COMMISSION' 'SELECT COUNT(*) AS N, CAST(SUM(Commission) AS decimal(18,6)) AS TotalCommission FROM OrderFills'
Run-Q 'POS_BY_SYMBOL' 'SELECT Symbol, COUNT(*) AS Trades, CAST(SUM(RealizedPnl) AS decimal(18,6)) AS Net, CAST(AVG(RealizedPnl) AS decimal(18,6)) AS Avg FROM Positions WHERE Status=2 GROUP BY Symbol'
Run-Q 'OPEN_POSITIONS' 'SELECT Id, Symbol, Side, Quantity, AverageEntryPrice, UnrealizedPnl, CAST(AverageEntryPrice*Quantity AS decimal(18,6)) AS Notional FROM Positions WHERE Status=1'
Run-Q 'BALANCE' 'SELECT Id, Mode, StartingBalance, CurrentBalance, UpdatedAt FROM VirtualBalances'
Run-Q 'POS_TODAY_UTC' "SELECT COUNT(*) AS N, CAST(SUM(RealizedPnl) AS decimal(18,6)) AS TotalToday FROM Positions WHERE Status=2 AND ClosedAt >= CAST(CAST(SYSUTCDATETIME() AS date) AS datetimeoffset)"
Run-Q 'POS_24H' "SELECT COUNT(*) AS N, CAST(SUM(RealizedPnl) AS decimal(18,6)) AS Total24h FROM Positions WHERE Status=2 AND ClosedAt >= DATEADD(hour,-24,SYSUTCDATETIME())"
Run-Q 'POS_1H' "SELECT COUNT(*) AS N, CAST(SUM(RealizedPnl) AS decimal(18,6)) AS Total1h FROM Positions WHERE Status=2 AND ClosedAt >= DATEADD(hour,-1,SYSUTCDATETIME())"
Run-Q 'POS_WIN_LOSS' "SELECT SUM(CASE WHEN RealizedPnl>0 THEN 1 ELSE 0 END) AS Wins, SUM(CASE WHEN RealizedPnl<=0 THEN 1 ELSE 0 END) AS Losses, CAST(SUM(CASE WHEN RealizedPnl>0 THEN RealizedPnl ELSE 0 END) AS decimal(18,6)) AS GrossWin, CAST(SUM(CASE WHEN RealizedPnl<=0 THEN RealizedPnl ELSE 0 END) AS decimal(18,6)) AS GrossLoss FROM Positions WHERE Status=2"
Run-Q 'CLOSED_TRADES_DETAIL' 'SELECT TOP 30 Id, Symbol, Side, Quantity, AverageEntryPrice, ExitPrice, RealizedPnl, OpenedAt, ClosedAt FROM Positions WHERE Status=2 ORDER BY ClosedAt DESC'
Run-Q 'FILLS_PER_POSITION' 'SELECT TOP 20 p.Id AS PosId, p.Symbol, p.RealizedPnl AS Pnl, SUM(f.Commission) AS FeeTotal, COUNT(f.Id) AS FillCount FROM Positions p LEFT JOIN Orders o ON o.PositionId = p.Id LEFT JOIN OrderFills f ON f.OrderId = o.Id WHERE p.Status=2 GROUP BY p.Id, p.Symbol, p.RealizedPnl, p.ClosedAt ORDER BY p.ClosedAt DESC'
Run-Q 'ORDER_COLUMNS' "SELECT TOP 3 COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Orders' AND COLUMN_NAME IN ('Commission','PositionId','Mode')"

$conn.Close()
Write-Host 'DONE'
