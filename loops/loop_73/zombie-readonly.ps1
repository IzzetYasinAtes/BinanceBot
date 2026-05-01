$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

Write-Host "=== PositionClosed event'lerinden gercek Realized (Loop 72) ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT PayloadJson FROM SystemEvents WHERE EventType = 'PositionClosed' AND OccurredAt > '2026-05-01 04:07' ORDER BY OccurredAt"
$r = $cmd.ExecuteReader()
$totalEventPnl = 0
$count = 0
while ($r.Read()) {
    try {
        $obj = $r['PayloadJson'] | ConvertFrom-Json
        $posId = [int]$obj.details.positionId
        $pnl = [decimal]$obj.details.realizedPnl
        $sym = $obj.details.symbol
        Write-Host ("  $sym Id=$posId pnl=`$$([math]::Round($pnl, 4))")
        $totalEventPnl += $pnl
        $count++
    } catch {}
}
$r.Close()
Write-Host ""
Write-Host ("Toplam $count PositionClosed event = `$$([math]::Round($totalEventPnl, 4))")

Write-Host ""
Write-Host "=== Position table actual sayim ==="
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Status, COUNT(*) AS Cnt, ISNULL(SUM(RealizedPnl), 0) AS Total FROM Positions WHERE OpenedAt > '2026-05-01 04:07' GROUP BY Status"
$r2 = $cmd2.ExecuteReader()
while ($r2.Read()) {
    Write-Host ("  Status=$($r2['Status']) Count=$($r2['Cnt']) RealizedSum=`$$($r2['Total'])")
}
$r2.Close()

Write-Host ""
Write-Host "=== Discrepancy: Event toplami vs Position table farki = ZOMBI Cost ==="

$conn.Close()
