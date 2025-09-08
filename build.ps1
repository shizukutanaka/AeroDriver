#!/usr/bin/env pwsh
param(
    [string]$Configuration = "Debug",
    [switch]$Clean,
    [switch]$Test,
    [switch]$Pack,
    [switch]$Publish,
    [switch]$Analyze,
    [string]$Runtime = "win-x64",
    [string]$Target = "Build"
)

# Colors for output
$Red = [System.ConsoleColor]::Red
$Green = [System.ConsoleColor]::Green
$Yellow = [System.ConsoleColor]::Yellow
$Blue = [System.ConsoleColor]::Blue

function Write-ColorOutput($ForegroundColor) {
    if ($Host.UI.RawUI.ForegroundColor -ne $null) {
        $originalForegroundColor = $Host.UI.RawUI.ForegroundColor
        $Host.UI.RawUI.ForegroundColor = $ForegroundColor
    }
    
    $args | ForEach-Object { Write-Output $_ }
    
    if ($originalForegroundColor -ne $null) {
        $Host.UI.RawUI.ForegroundColor = $originalForegroundColor
    }
}

# Header
Write-ColorOutput $Blue "AeroDriver Build Script"
Write-ColorOutput $Blue "======================"

# Check if dotnet CLI is installed
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-ColorOutput $Red "Error: .NET CLI is not installed or not in PATH"
    exit 1
}

# Show dotnet info
Write-ColorOutput $Yellow "Using .NET:"
dotnet --version

try {
    if ($Clean) {
        Write-ColorOutput $Yellow "Cleaning solution..."
        dotnet clean --configuration $Configuration --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            Write-ColorOutput $Red "Clean failed"
            exit $LASTEXITCODE
        }
    }

    Write-ColorOutput $Yellow "Restoring packages..."
    dotnet restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Write-ColorOutput $Red "Restore failed"
        exit $LASTEXITCODE
    }

    Write-ColorOutput $Yellow "Building solution ($Configuration)..."
    dotnet build --configuration $Configuration --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Write-ColorOutput $Red "Build failed"
        exit $LASTEXITCODE
    }

    if ($Test) {
        Write-ColorOutput $Yellow "Running tests..."
        dotnet test --configuration $Configuration --no-build --verbosity minimal --logger "console;verbosity=minimal"
        if ($LASTEXITCODE -ne 0) {
            Write-ColorOutput $Red "Tests failed"
            exit $LASTEXITCODE
        }
    }

    if ($Pack) {
        Write-ColorOutput $Yellow "Creating packages..."
        dotnet pack --configuration $Configuration --no-build --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            Write-ColorOutput $Red "Pack failed"
            exit $LASTEXITCODE
        }
    }

    if ($Publish) {
        Write-ColorOutput $Yellow "Publishing application for $Runtime..."
        dotnet publish src/AeroDriver.CLI/AeroDriver.CLI.csproj --configuration $Configuration --runtime $Runtime --self-contained false --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            Write-ColorOutput $Red "Publish failed"
            exit $LASTEXITCODE
        }
        Write-ColorOutput $Green "Published to: src/AeroDriver.CLI/bin/$Configuration/net8.0/$Runtime/publish/"
    }

    if ($Analyze) {
        Write-ColorOutput $Yellow "Running code analysis..."
        
        # Run format check
        dotnet format --verify-no-changes --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            Write-ColorOutput $Yellow "Code formatting issues detected. Run 'dotnet format' to fix."
        }

        # Run security analysis if available
        if (Get-Command "dotnet-security" -ErrorAction SilentlyContinue) {
            Write-ColorOutput $Yellow "Running security analysis..."
            dotnet security --project .
        }
    }

    Write-ColorOutput $Green "Build completed successfully!"

} catch {
    Write-ColorOutput $Red "Build failed with error: $($_.Exception.Message)"
    exit 1
}