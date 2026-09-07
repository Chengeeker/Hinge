# Run all Phase 0 test suites
Write-Host "=== Running Windows Test Suite ===" -ForegroundColor Cyan
dotnet test windows/Hinge.sln
if ($LASTEXITCODE -ne 0) {
    Write-Error "Windows tests failed!"
    exit $LASTEXITCODE
}

Write-Host "=== Running Android Flutter Test Suite ===" -ForegroundColor Cyan
Push-Location android
& "D:\flutter_sdk\bin\flutter.bat" test
$flutterExit = $LASTEXITCODE
Pop-Location

if ($flutterExit -ne 0) {
    Write-Error "Android tests failed!"
    exit $flutterExit
}

Write-Host "=== All Phase 0, 1, 2, 3, 4, 5, 6, 7, 8 & 9 Tests Passed Successfully! ===" -ForegroundColor Green
