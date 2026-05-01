$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

$cmd = $conn.CreateCommand()
$cmd.CommandText = @"
INSERT INTO VirtualBalances (Mode, StartingBalance, CurrentBalance, Equity, IterationId, StartedAt, LastResetAt, ResetCount, UpdatedAt)
VALUES (1, 500.0, 500.0, 500.0, NEWID(), SYSUTCDATETIME(), SYSUTCDATETIME(), 0, SYSUTCDATETIME())
"@
$cmd.ExecuteNonQuery() | Out-Null
Write-Host "VirtualBalances seeded: Mode=1 (Paper) Starting=500"

$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Mode, StartingBalance, CurrentBalance, Equity FROM VirtualBalances"
$r = $cmd2.ExecuteReader()
while ($r.Read()) {
    Write-Host ('  Mode=' + $r['Mode'] + ' Start=' + $r['StartingBalance'] + ' Cur=' + $r['CurrentBalance'] + ' Eq=' + $r['Equity'])
}
$r.Close()

$conn.Close()
