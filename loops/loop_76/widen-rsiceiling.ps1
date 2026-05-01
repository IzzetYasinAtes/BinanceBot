$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

# RsiNeutralCeiling 60 -> 70 (RSI Zone genis pencere, overbought rejim de yakalar)
$cmd = $conn.CreateCommand()
$cmd.CommandText = "UPDATE Strategies SET ParametersJson = REPLACE(ParametersJson, '`"RsiNeutralCeiling`":60,', '`"RsiNeutralCeiling`":70,'), UpdatedAt = SYSUTCDATETIME() WHERE Name LIKE '%-KMS'"
$rows = $cmd.ExecuteNonQuery()
Write-Host "RsiNeutralCeiling 60 -> 70: $rows row(s) updated"

$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Name, ParametersJson FROM Strategies WHERE Name LIKE '%-KMS' ORDER BY Name"
$r = $cmd2.ExecuteReader()
while ($r.Read()) {
    $j = $r['ParametersJson'] | ConvertFrom-Json
    Write-Host ('  ' + $r['Name'] + ' RsiOver=' + $j.RsiOversoldZone + ' RsiCeil=' + $j.RsiNeutralCeiling + ' MinScore=' + $j.MinScoreThreshold)
}
$r.Close()

$conn.Close()
