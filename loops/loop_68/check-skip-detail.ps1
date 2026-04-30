$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

Write-Host "=== Son 10 SignalSkipped tam payload ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT TOP 10 OccurredAt, PayloadJson FROM SystemEvents WHERE EventType = 'SignalSkipped' AND OccurredAt > '2026-04-30 21:31' ORDER BY OccurredAt DESC"
$r = $cmd.ExecuteReader()
$idx = 1
while ($r.Read()) {
    Write-Host ""
    Write-Host ("--- $idx [" + $r['OccurredAt'] + '] ---')
    Write-Host $r['PayloadJson']
    $idx++
}
$r.Close()

$conn.Close()
