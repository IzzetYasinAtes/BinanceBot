$cs = 'Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True'
$conn = New-Object System.Data.SqlClient.SqlConnection $cs
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT TOP 8 LEFT(PayloadJson, 400) FROM SystemEvents WHERE EventType = 'SignalSkipped' AND OccurredAt > DATEADD(MINUTE, -10, SYSUTCDATETIME()) ORDER BY OccurredAt DESC"
$r = $cmd.ExecuteReader()
while ($r.Read()) { Write-Output $r.GetString(0); Write-Output '---' }
$r.Close()
$conn.Close()
