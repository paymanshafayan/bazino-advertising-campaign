<#
  ساخت محلی برنامه (اختیاری؛ CI همین کار را انجام می‌دهد).
  پیش‌نیاز: .NET SDK 10  →  winget install Microsoft.DotNet.SDK.10
  اجرا:  pwsh -File scripts/publish.ps1
  خروجی: out/publish/BazinoMarketing.exe  (self-contained, single-file, win-x64)
#>
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

dotnet test tests/BazinoMarketing.Core.Tests/BazinoMarketing.Core.Tests.csproj -c Release
dotnet publish src/BazinoMarketing.App/BazinoMarketing.App.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none -p:DebugSymbols=false -o out/publish

if ($args -contains "-Screenshots") {
  New-Item -ItemType Directory -Force out/shots | Out-Null
  & out/publish/BazinoMarketing.exe --render-screenshots (Resolve-Path out/shots)
  Get-ChildItem out/shots
}
Write-Host "Done: out/publish/BazinoMarketing.exe"
