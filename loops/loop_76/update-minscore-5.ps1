$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

# Loop 76 binance-expert oneri: MinScoreThreshold 4 -> 5 (entry kalitesi)
$cmd = $conn.CreateCommand()
$cmd.CommandText = "UPDATE Strategies SET ParametersJson = REPLACE(ParametersJson, '`"MinScoreThreshold`":4,', '`"MinScoreThreshold`":5,'), UpdatedAt = SYSUTCDATETIME() WHERE Name LIKE '%-KMS'"
$rows = $cmd.ExecuteNonQuery()
Write-Host "MinScoreThreshold 4 -> 5: $rows row(s) updated"

$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Name, ParametersJson FROM Strategies WHERE Name LIKE '%-KMS' ORDER BY Name"
$r = $cmd2.ExecuteReader()
while ($r.Read()) {
    $j = $r['ParametersJson'] | ConvertFrom-Json
    Write-Host ('  ' + $r['Name'] + ' MinScore=' + $j.MinScoreThreshold + ' RsiCeil=' + $j.RsiNeutralCeiling + ' TpMul=' + $j.TpAtrMultiplier)
}
$r.Close()

$conn.Close()
