$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

# Cumulative L71+L72 carry-over: +$0.31
$cmd = $conn.CreateCommand()
$cmd.CommandText = @"
INSERT INTO VirtualBalances (Id, Mode, StartingBalance, CurrentBalance, Equity, IterationId, StartedAt, LastResetAt, ResetCount, UpdatedAt)
VALUES (1, 1, 500.0, 500.31, 500.31, NEWID(), SYSUTCDATETIME(), SYSUTCDATETIME(), 2, SYSUTCDATETIME())
"@
$cmd.ExecuteNonQuery() | Out-Null
Write-Host "VirtualBalance carry-over seeded: Starting=500, Current=500.31, Equity=500.31"

$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Id, StartingBalance, CurrentBalance FROM VirtualBalances"
$r = $cmd2.ExecuteReader()
while ($r.Read()) {
    Write-Host ('  Id=' + $r['Id'] + ' Start=' + $r['StartingBalance'] + ' Cur=' + $r['CurrentBalance'])
}
$r.Close()

$conn.Close()
