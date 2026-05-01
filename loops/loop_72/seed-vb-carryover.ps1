$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

# Carry over Loop 71 Realized: $0.85 -> Starting $500, Current $500.85
$cmd = $conn.CreateCommand()
$cmd.CommandText = @"
INSERT INTO VirtualBalances (Id, Mode, StartingBalance, CurrentBalance, Equity, IterationId, StartedAt, LastResetAt, ResetCount, UpdatedAt)
VALUES (1, 1, 500.0, 500.85, 500.85, NEWID(), SYSUTCDATETIME(), SYSUTCDATETIME(), 1, SYSUTCDATETIME())
"@
$cmd.ExecuteNonQuery() | Out-Null
Write-Host "VirtualBalance seeded with carry-over: Starting=500, Current=500.85, Equity=500.85"

$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Id, Mode, StartingBalance, CurrentBalance, Equity FROM VirtualBalances"
$r = $cmd2.ExecuteReader()
while ($r.Read()) {
    Write-Host ('  Id=' + $r['Id'] + ' Mode=' + $r['Mode'] + ' Start=' + $r['StartingBalance'] + ' Cur=' + $r['CurrentBalance'] + ' Eq=' + $r['Equity'])
}
$r.Close()
$conn.Close()
