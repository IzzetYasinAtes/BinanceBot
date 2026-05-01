$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

# Loop 78: BbwHardGate=true ekle (yeni param, mevcut JSON'lara inject)
# JSON'da BbwHardGate yoksa BbwScorePoints'in onune ekle
$cmd = $conn.CreateCommand()
$cmd.CommandText = "UPDATE Strategies SET ParametersJson = REPLACE(ParametersJson, '`"BbwScorePoints`":1', '`"BbwHardGate`":true,`"BbwScorePoints`":1'), UpdatedAt = SYSUTCDATETIME() WHERE Name LIKE '%-KMS' AND ParametersJson NOT LIKE '%BbwHardGate%'"
$rows = $cmd.ExecuteNonQuery()
Write-Host "BbwHardGate=true added: $rows row(s)"

$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Name, ParametersJson FROM Strategies WHERE Name LIKE '%-KMS' ORDER BY Name"
$r = $cmd2.ExecuteReader()
while ($r.Read()) {
    $j = $r['ParametersJson'] | ConvertFrom-Json
    $bbwHardGate = if ($j.BbwHardGate -ne $null) { $j.BbwHardGate } else { 'MISSING' }
    Write-Host ('  ' + $r['Name'] + ' BbwHardGate=' + $bbwHardGate + ' BbwThr=' + $j.BbwThreshold + ' MinScore=' + $j.MinScoreThreshold)
}
$r.Close()

$conn.Close()
