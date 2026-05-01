$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

Write-Host "=== Tum Positions (Loop 71 boot 01:54 UTC sonrasi) ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT Id, Symbol, Status, OpenedAt, ClosedAt, AverageEntryPrice, ExitPrice, Quantity, RealizedPnl, EntryCommission, ExitCommission FROM Positions WHERE OpenedAt > '2026-05-01 01:54' ORDER BY OpenedAt"
$r = $cmd.ExecuteReader()
$total = 0
while ($r.Read()) {
    $rpnl = if ($r['RealizedPnl'] -ne [DBNull]::Value) { [decimal]$r['RealizedPnl'] } else { 0 }
    $total += $rpnl
    Write-Host ("  Id=$($r['Id']) $($r['Symbol']) Status=$($r['Status']) Opened=$($r['OpenedAt']) Closed=$($r['ClosedAt']) Entry=$($r['AverageEntryPrice']) Exit=$($r['ExitPrice']) Qty=$($r['Quantity']) PnL=`$$([math]::Round($rpnl, 4))")
}
$r.Close()
Write-Host ""
Write-Host "Sum: `$$([math]::Round($total, 4))"

Write-Host ""
Write-Host "=== Aggregate ==="
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Status, COUNT(*) AS Cnt, ISNULL(SUM(RealizedPnl), 0) AS Total FROM Positions WHERE OpenedAt > '2026-05-01 01:54' GROUP BY Status"
$r2 = $cmd2.ExecuteReader()
while ($r2.Read()) {
    Write-Host ("  Status=$($r2['Status']) Count=$($r2['Cnt']) RealizedSum=`$$($r2['Total'])")
}
$r2.Close()

$conn.Close()
