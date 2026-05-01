$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

Write-Host "=== Position table BE field var mi? ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Positions' AND COLUMN_NAME LIKE '%Break%'"
$r = $cmd.ExecuteReader()
while ($r.Read()) {
    Write-Host ('  ' + $r['COLUMN_NAME'] + ' (' + $r['DATA_TYPE'] + ')')
}
$r.Close()

Write-Host ""
Write-Host "=== Loop 75 acik pozisyon BE state ==="
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Id, Symbol, Status, AverageEntryPrice, MarkPrice, StopPrice, BreakEvenAppliedAt FROM Positions WHERE OpenedAt > '2026-05-01 09:00' ORDER BY OpenedAt DESC"
$r2 = $cmd2.ExecuteReader()
while ($r2.Read()) {
    $beApplied = if ($r2['BreakEvenAppliedAt'] -ne [DBNull]::Value) { $r2['BreakEvenAppliedAt'] } else { 'NULL' }
    Write-Host ("  Id=$($r2['Id']) $($r2['Symbol']) Status=$($r2['Status']) Entry=$($r2['AverageEntryPrice']) Mark=$($r2['MarkPrice']) Stop=$($r2['StopPrice']) BeApplied=$beApplied")
}
$r2.Close()

$conn.Close()
