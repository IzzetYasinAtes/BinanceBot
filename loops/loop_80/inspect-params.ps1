$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT TOP 1 Name, ParametersJson FROM Strategies WHERE Name = 'BTC-KMS'"
$r = $cmd.ExecuteReader()
if ($r.Read()) {
    Write-Host "=== BTC-KMS full ParametersJson ==="
    Write-Host $r['ParametersJson']
}
$r.Close()
$conn.Close()
