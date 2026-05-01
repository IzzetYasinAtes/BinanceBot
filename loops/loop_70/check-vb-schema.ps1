$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='VirtualBalances' ORDER BY ORDINAL_POSITION"
$r = $cmd.ExecuteReader()
while ($r.Read()) { Write-Host ('  ' + $r['COLUMN_NAME'] + ' (' + $r['DATA_TYPE'] + ')') }
$r.Close()
$conn.Close()
