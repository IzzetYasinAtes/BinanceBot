$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

Write-Host "=== Tum Loop 74 PositionClosed event detayi (kronolojik) ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT OccurredAt, PayloadJson FROM SystemEvents WHERE EventType = 'PositionClosed' AND OccurredAt > '2026-05-01 08:30' ORDER BY OccurredAt"
$r = $cmd.ExecuteReader()
$idx = 1
$total = 0
while ($r.Read()) {
    try {
        $obj = $r['PayloadJson'] | ConvertFrom-Json
        $sym = $obj.details.symbol
        $pnl = [decimal]$obj.details.realizedPnl
        $reason = $obj.details.reason
        $occurred = $r['OccurredAt']
        Write-Host ("$idx. [$occurred] $sym pnl=`$$([math]::Round($pnl, 4)) reason=$reason")
        $total += $pnl
        $idx++
    } catch {}
}
$r.Close()
Write-Host ""
Write-Host ("TOTAL Realized = `$$([math]::Round($total, 4))")

Write-Host ""
Write-Host "=== RiskAlert detay ==="
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT OccurredAt, PayloadJson FROM SystemEvents WHERE EventType = 'RiskAlert' AND OccurredAt > '2026-05-01 08:30'"
$r2 = $cmd2.ExecuteReader()
while ($r2.Read()) {
    Write-Host ("[" + $r2['OccurredAt'] + "] " + $r2['PayloadJson'])
}
$r2.Close()

$conn.Close()
