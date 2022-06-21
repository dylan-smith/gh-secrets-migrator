#!/usr/bin/env pwsh

$AssemblyVersion = "9.9"

if ((Test-Path env:CLI_VERSION) -And $env:CLI_VERSION.StartsWith("refs/tags/v")) {
    $AssemblyVersion = $env:CLI_VERSION.Substring(11)
}

Write-Output "version: $AssemblyVersion"

if ((Test-Path env:SKIP_WINDOWS) -And $env:SKIP_WINDOWS.ToUpper() -eq "TRUE") {
    Write-Output "Skipping Windows build because SKIP_WINDOWS is set"
}
else {
    dotnet publish src/SecretsMigrator.csproj -c Release -o dist/win-x64/ -r win-x64 -p:PublishSingleFile=true -p:PublishTrimmed=true --self-contained true /p:DebugType=None /p:IncludeNativeLibrariesForSelfExtract=true /p:VersionPrefix=$AssemblyVersion

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    if (Test-Path -Path ./dist/win-x64/secrets-migrator-windows-amd64.exe) {
        Remove-Item ./dist/win-x64/secrets-migrator-windows-amd64.exe
    }

    Rename-Item ./dist/win-x64/SecretsMigrator.exe secrets-migrator-windows-amd64.exe
}

if ((Test-Path env:SKIP_LINUX) -And $env:SKIP_LINUX.ToUpper() -eq "TRUE") {
    Write-Output "Skipping Linux build because SKIP_LINUX is set"
}
else {
    dotnet publish src/SecretsMigrator.csproj -c Release -o dist/linux-x64/ -r linux-x64 -p:PublishSingleFile=true -p:PublishTrimmed=true --self-contained true /p:DebugType=None /p:IncludeNativeLibrariesForSelfExtract=true /p:VersionPrefix=$AssemblyVersion

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    if (Test-Path -Path ./dist/linux-x64/secrets-migrator-linux-amd64) {
        Remove-Item ./dist/linux-x64/secrets-migrator-linux-amd64
    }

    Rename-Item ./dist/linux-x64/SecretsMigrator secrets-migrator-linux-amd64
}

if ((Test-Path env:SKIP_MACOS) -And $env:SKIP_MACOS.ToUpper() -eq "TRUE") {
    Write-Output "Skipping MacOS build because SKIP_MACOS is set"
}
else {
    dotnet publish src/SecretsMigrator.csproj -c Release -o dist/osx-x64/ -r osx-x64 -p:PublishSingleFile=true -p:PublishTrimmed=true --self-contained true /p:DebugType=None /p:IncludeNativeLibrariesForSelfExtract=true /p:VersionPrefix=$AssemblyVersion

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    if (Test-Path -Path ./dist/osx-x64/secrets-migrator-darwin-amd64) {
        Remove-Item ./dist/osx-x64/secrets-migrator-darwin-amd64
    }

    Rename-Item ./dist/osx-x64/SecretsMigrator secrets-migrator-darwin-amd64
}
