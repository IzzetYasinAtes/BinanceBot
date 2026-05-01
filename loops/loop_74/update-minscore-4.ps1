$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

# Loop 74.5: MinScore 5 -> 4 (Loop 73 patterni geri, RsiCeiling 50 koruyor)
Write-Host "=== UPDATE MinScoreThreshold 5 -> 4 ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "UPDATE Strategies SET ParametersJson = REPLACE(ParametersJson, '`"MinScoreThreshold`":5,', '`"MinScoreThreshold`":4,'), UpdatedAt = SYSUTCDATETIME() WHERE Name LIKE '%-KMS'"
$rows = $cmd.ExecuteNonQuery()
Write-Host "Updated $rows row(s)"

Write-Host ""
Write-Host "=== Verify ==="
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Name, ParametersJson FROM Strategies WHERE Name LIKE '%-KMS' ORDER BY Name"
$r = $cmd2.ExecuteReader()
while ($r.Read()) {
    $j = $r['ParametersJson'] | ConvertFrom-Json
    Write-Host ("  " + $r['Name'] + " MinScore=" + $j.MinScoreThreshold + " RsiCeiling=" + $j.RsiNeutralCeiling + " TpMul=" + $j.TpAtrMultiplier)
}
$r.Close()

$conn.Close()
