@echo off
setlocal

set PYTHONIOENCODING=utf-8

echo === dotnet restore ===
dotnet restore ScanVault.sln -v:minimal
if errorlevel 1 exit /b %errorlevel%

echo === dotnet build ===
dotnet build ScanVault.sln --configuration Release -m:1 -v:minimal
if errorlevel 1 exit /b %errorlevel%

echo === dotnet test ===
dotnet test ScanVault.sln --configuration Release --no-build -m:1 -v:minimal
if errorlevel 1 exit /b %errorlevel%

echo === dotnet format verify ===
dotnet format ScanVault.sln --verify-no-changes --no-restore
if errorlevel 1 exit /b %errorlevel%

echo === git diff check ===
git diff --check
if errorlevel 1 exit /b %errorlevel%

echo === code-review-graph update ===
code-review-graph update --brief
if errorlevel 1 exit /b %errorlevel%

echo === code-review-graph detect changes ===
code-review-graph detect-changes --base HEAD --brief
if errorlevel 1 exit /b %errorlevel%

echo === graphify update ===
graphify update
if errorlevel 1 exit /b %errorlevel%

echo === all checks passed ===
exit /b 0
