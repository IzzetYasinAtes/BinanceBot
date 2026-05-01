$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

# Loop 76.5: MinScoreThreshold 5 -> 4 (Loop 74 patterni - 5 katı)
$cmd = $conn.CreateCommand()
$cmd.CommandText = "UPDATE Strategies SET ParametersJson = REPLACE(ParametersJson, '`"MinScoreThreshold`":5,', '`"MinScoreThreshold`":4,'), UpdatedAt = SYSUTCDATETIME() WHERE Name LIKE '%-KMS'"
$rows = $cmd.ExecuteNonQuery()
Write-Host "MinScoreThreshold 5 -> 4: $rows row(s) updated"

$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Name, ParametersJson FROM Strategies WHERE Name LIKE '%-KMS' ORDER BY Name"
$r = $cmd2.ExecuteReader()
while ($r.Read()) {
    $j = $r['ParametersJson'] | ConvertFrom-Json
    Write-Host ('  ' + $r['Name'] + ' MinScore=' + $j.MinScoreThreshold + ' RsiCeil=' + $j.RsiNeutralCeiling)
}
$r.Close()

$conn.Close()
