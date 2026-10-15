param([switch]$Publish, [switch]$Test, [switch]$SkipDirect)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = Join-Path $taskRoot '.tools/packages'
$taskSdk = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $taskSdk)) {
    $taskDotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $taskDotnet) { throw 'Install the .NET 10 SDK and make dotnet available on PATH to build from source. The standalone EXE does not require the SDK.' }
    $taskSdk = $taskDotnet.Source
}
$taskProject = Join-Path $taskRoot 'src/CodexAccounts/CodexAccounts.csproj'
& $taskSdk restore $taskProject --configfile (Join-Path $taskRoot 'NuGet.Config') -p:NuGetAudit=false --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
& $taskSdk build $taskProject --no-restore -c Release --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($Test) {
    $taskTests = Join-Path $taskRoot 'tests/CodexAccounts.Tests/CodexAccounts.Tests.csproj'
    & $taskSdk restore $taskTests --configfile (Join-Path $taskRoot 'NuGet.Config') -p:NuGetAudit=false --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Test restore failed.' }
    & $taskSdk run --project $taskTests --no-restore -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
if ($Publish) {
    $taskNuget = Join-Path $taskRoot 'NuGet.Config'
    $taskVersion = ([xml](Get-Content -LiteralPath $taskProject -Raw)).Project.PropertyGroup.Version
    $taskPublishName = "CodexAccounts-$taskVersion"
    $taskPublish = Join-Path $taskRoot ('artifacts/' + $taskPublishName)
    & $taskSdk publish $taskProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:NuGetAudit=false "-p:RestoreConfigFile=$taskNuget" -o $taskPublish --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $taskLicenses = Join-Path $taskPublish 'licenses'
    New-Item -ItemType Directory -Path $taskLicenses -Force | Out-Null
    foreach ($taskName in @('README.md','START_HERE.md','THIRD_PARTY_NOTICES.md','LICENSE','Return to official Codex.cmd')) {
        Copy-Item -LiteralPath (Join-Path $taskRoot $taskName) -Destination $taskPublish -Force
    }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'START_HERE.md') -Destination (Join-Path $taskPublish 'START_HERE.txt') -Force
    Get-ChildItem -LiteralPath (Join-Path $taskRoot 'licenses') -Filter '*.txt' | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $taskLicenses -Force }
    $taskSdkRoot = Split-Path -Parent $taskSdk
    foreach ($taskName in @('LICENSE.txt','ThirdPartyNotices.txt')) {
        $taskSource = Join-Path $taskSdkRoot $taskName
        if (Test-Path -LiteralPath $taskSource) { Copy-Item -LiteralPath $taskSource -Destination (Join-Path $taskLicenses ('Microsoft.NET-' + $taskName)) -Force }
    }
    $taskExeHash = (Get-FileHash -LiteralPath (Join-Path $taskPublish 'CodexAccounts.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not $SkipDirect) {
        Copy-Item -LiteralPath (Join-Path $taskPublish 'CodexAccounts.exe') -Destination (Join-Path $taskRoot 'CodexAccounts.exe') -Force
        ($taskExeHash + '  CodexAccounts.exe') | Set-Content -LiteralPath (Join-Path $taskRoot 'CodexAccounts.exe.sha256') -Encoding ascii
    }
    ($taskExeHash + '  CodexAccounts.exe') | Set-Content -LiteralPath (Join-Path $taskPublish 'SHA256SUMS.txt') -Encoding ascii
    $taskZipName = "CodexAccounts-$taskVersion-win-x64.zip"
    $taskZip = Join-Path $taskRoot ('artifacts/' + $taskZipName)
    Compress-Archive -LiteralPath $taskPublish -DestinationPath $taskZip -CompressionLevel Optimal -Force
    $taskZipHash = (Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash.ToLowerInvariant()
    @("$taskExeHash  $taskPublishName/CodexAccounts.exe", "$taskZipHash  $taskZipName") | Set-Content -LiteralPath (Join-Path $taskRoot 'artifacts/SHA256SUMS.txt') -Encoding ascii
}
