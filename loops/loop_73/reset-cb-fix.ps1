$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

# Enum: Healthy=1, Cooldown=2, Tripped=3
Write-Host "=== Mevcut RiskProfile CB state ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT Id, CircuitBreakerStatus FROM RiskProfiles ORDER BY Id"
$r = $cmd.ExecuteReader()
while ($r.Read()) {
    Write-Host ('  Id=' + $r['Id'] + ' CB=' + $r['CircuitBreakerStatus'])
}
$r.Close()

Write-Host ""
Write-Host "=== TUM RiskProfile -> Healthy (1) reset ==="
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "UPDATE RiskProfiles SET CircuitBreakerStatus = 1, UpdatedAt = SYSUTCDATETIME()"
$rows = $cmd2.ExecuteNonQuery()
Write-Host "Reset $rows row(s) -> Healthy=1"

Write-Host ""
Write-Host "=== Verify ==="
$cmd3 = $conn.CreateCommand()
$cmd3.CommandText = "SELECT Id, CircuitBreakerStatus FROM RiskProfiles ORDER BY Id"
$r3 = $cmd3.ExecuteReader()
while ($r3.Read()) {
    Write-Host ('  Id=' + $r3['Id'] + ' CB=' + $r3['CircuitBreakerStatus'] + ' (1=Healthy)')
}
$r3.Close()

$conn.Close()
