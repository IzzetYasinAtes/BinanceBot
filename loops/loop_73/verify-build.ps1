# Loop 73 — verify-build.ps1
# Bot durduktan sonra çalıştırılacak. Solution build + ilgili testleri koşar.
# Bot çalışırken Api dll lock nedeniyle bu adım atlanır; PM bot stop yaptıktan
# sonra çağırır.

$ErrorActionPreference = 'Stop'

Write-Host "=== dotnet build BinanceBot.sln ==="
dotnet build "D:/repos/BinanceBot/BinanceBot.sln" -c Debug --nologo
if ($LASTEXITCODE -ne 0) { throw "BUILD FAILED" }

Write-Host ""
Write-Host "=== dotnet test (Loop 73 zombi-position regression filter) ==="
dotnet test "D:/repos/BinanceBot/tests/Tests/BinanceBot.Tests.csproj" `
    -c Debug --nologo --no-build `
    --filter "FullyQualifiedName~PositionCloseInvariantsTests|FullyQualifiedName~OrderFilledPositionHandlerTests|FullyQualifiedName~PositionFeeAwareTests"
if ($LASTEXITCODE -ne 0) { throw "TESTS FAILED" }

Write-Host ""
Write-Host "=== Done. Build + Loop 73 regression suite green. ==="
