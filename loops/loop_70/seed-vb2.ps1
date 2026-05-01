$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

# Check identity status
$cmd0 = $conn.CreateCommand()
$cmd0.CommandText = "SELECT COLUMNPROPERTY(OBJECT_ID('VirtualBalances'), 'Id', 'IsIdentity') AS IsId"
$r0 = $cmd0.ExecuteReader()
$r0.Read() | Out-Null
$isId = $r0['IsId']
$r0.Close()
Write-Host "Id IsIdentity: $isId"

if ($isId -eq 0) {
    # Manual Id assignment
    $cmd1 = $conn.CreateCommand()
    $cmd1.CommandText = @"
INSERT INTO VirtualBalances (Id, Mode, StartingBalance, CurrentBalance, Equity, IterationId, StartedAt, LastResetAt, ResetCount, UpdatedAt)
VALUES (1, 1, 500.0, 500.0, 500.0, NEWID(), SYSUTCDATETIME(), SYSUTCDATETIME(), 0, SYSUTCDATETIME())
"@
    $cmd1.ExecuteNonQuery() | Out-Null
    Write-Host "Inserted with Id=1"
} else {
    $cmd1 = $conn.CreateCommand()
    $cmd1.CommandText = @"
INSERT INTO VirtualBalances (Mode, StartingBalance, CurrentBalance, Equity, IterationId, StartedAt, LastResetAt, ResetCount, UpdatedAt)
VALUES (1, 500.0, 500.0, 500.0, NEWID(), SYSUTCDATETIME(), SYSUTCDATETIME(), 0, SYSUTCDATETIME())
"@
    $cmd1.ExecuteNonQuery() | Out-Null
    Write-Host "Inserted (identity)"
}

$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Id, Mode, StartingBalance, CurrentBalance, Equity FROM VirtualBalances"
$r = $cmd2.ExecuteReader()
while ($r.Read()) {
    Write-Host ('  Id=' + $r['Id'] + ' Mode=' + $r['Mode'] + ' Start=' + $r['StartingBalance'] + ' Cur=' + $r['CurrentBalance'] + ' Eq=' + $r['Equity'])
}
$r.Close()

$conn.Close()
