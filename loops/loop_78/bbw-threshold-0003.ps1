$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "UPDATE Strategies SET ParametersJson = REPLACE(ParametersJson, '`"BbwThreshold`":0.005', '`"BbwThreshold`":0.003'), UpdatedAt = SYSUTCDATETIME() WHERE Name LIKE '%-KMS'"
$rows = $cmd.ExecuteNonQuery()
Write-Host "BBW threshold 0.005 -> 0.003: $rows row(s) updated"

$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Name, ParametersJson FROM Strategies WHERE Name LIKE '%-KMS' ORDER BY Name"
$r = $cmd2.ExecuteReader()
while ($r.Read()) {
    $j = $r['ParametersJson'] | ConvertFrom-Json
    Write-Host ('  ' + $r['Name'] + ' BbwThr=' + $j.BbwThreshold + ' BbwHardGate=' + $j.BbwHardGate)
}
$r.Close()
$conn.Close()
