$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

foreach ($table in @('SystemEvents', 'Positions', 'Strategies')) {
    Write-Host "=== $table columns ==="
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='$table' ORDER BY ORDINAL_POSITION"
    $r = $cmd.ExecuteReader()
    while ($r.Read()) { Write-Host ('  ' + $r['COLUMN_NAME'] + ' (' + $r['DATA_TYPE'] + ')') }
    $r.Close()
    Write-Host ""
}

$conn.Close()
