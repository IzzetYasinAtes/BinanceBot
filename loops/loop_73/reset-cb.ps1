$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

Write-Host "=== Mevcut RiskProfile state ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT Id, Mode, CircuitBreakerStatus, MaxOpenPositions, MaxConsecutiveLosses FROM RiskProfiles"
$r = $cmd.ExecuteReader()
while ($r.Read()) {
    Write-Host ('  Id=' + $r['Id'] + ' Mode=' + $r['Mode'] + ' CB=' + $r['CircuitBreakerStatus'] + ' MaxOpen=' + $r['MaxOpenPositions'] + ' MaxConSL=' + $r['MaxConsecutiveLosses'])
}
$r.Close()

Write-Host ""
Write-Host "=== Reset CB Tripped -> Healthy (assume 0=Healthy, 1=Tripped) ==="
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "UPDATE RiskProfiles SET CircuitBreakerStatus = 0, UpdatedAt = SYSUTCDATETIME() WHERE CircuitBreakerStatus = 1"
$rows = $cmd2.ExecuteNonQuery()
Write-Host "Reset $rows row(s)"

Write-Host ""
Write-Host "=== Verify ==="
$cmd3 = $conn.CreateCommand()
$cmd3.CommandText = "SELECT Id, Mode, CircuitBreakerStatus FROM RiskProfiles"
$r3 = $cmd3.ExecuteReader()
while ($r3.Read()) {
    Write-Host ('  Id=' + $r3['Id'] + ' Mode=' + $r3['Mode'] + ' CB=' + $r3['CircuitBreakerStatus'])
}
$r3.Close()

$conn.Close()
