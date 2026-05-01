$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

Write-Host "=== Tum PositionClosed event'leri (Loop 71) ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT OccurredAt, PayloadJson FROM SystemEvents WHERE EventType = 'PositionClosed' AND OccurredAt > '2026-05-01 01:54' ORDER BY OccurredAt"
$r = $cmd.ExecuteReader()
while ($r.Read()) {
    Write-Host ("[" + $r['OccurredAt'] + "] " + $r['PayloadJson'].Substring(0, [Math]::Min(220, $r['PayloadJson'].Length)))
}
$r.Close()

Write-Host ""
Write-Host "=== Tum PositionOpened event'leri (Loop 71) ==="
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT OccurredAt, PayloadJson FROM SystemEvents WHERE EventType = 'PositionOpened' AND OccurredAt > '2026-05-01 01:54' ORDER BY OccurredAt"
$r2 = $cmd2.ExecuteReader()
while ($r2.Read()) {
    Write-Host ("[" + $r2['OccurredAt'] + "] " + $r2['PayloadJson'].Substring(0, [Math]::Min(220, $r2['PayloadJson'].Length)))
}
$r2.Close()

$conn.Close()
