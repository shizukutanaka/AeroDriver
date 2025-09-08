#!/bin/bash

# AeroDriver Build Script
set -e

# Default values
CONFIGURATION="Debug"
CLEAN=false
TEST=false
PACK=false
PUBLISH=false
ANALYZE=false
RUNTIME="linux-x64"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_color() {
    printf "${1}${2}${NC}\n"
}

# Show usage
show_help() {
    echo "AeroDriver Build Script"
    echo "Usage: ./build.sh [options]"
    echo ""
    echo "Options:"
    echo "  -c, --configuration  Build configuration (Debug|Release) [default: Debug]"
    echo "  --clean              Clean before building"
    echo "  --test               Run tests after building"
    echo "  --pack               Create NuGet packages"
    echo "  --publish            Publish application"
    echo "  --analyze            Run code analysis"
    echo "  -r, --runtime        Target runtime [default: linux-x64]"
    echo "  -h, --help           Show this help message"
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -c|--configuration)
            CONFIGURATION="$2"
            shift 2
            ;;
        --clean)
            CLEAN=true
            shift
            ;;
        --test)
            TEST=true
            shift
            ;;
        --pack)
            PACK=true
            shift
            ;;
        --publish)
            PUBLISH=true
            shift
            ;;
        --analyze)
            ANALYZE=true
            shift
            ;;
        -r|--runtime)
            RUNTIME="$2"
            shift 2
            ;;
        -h|--help)
            show_help
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            show_help
            exit 1
            ;;
    esac
done

print_color $BLUE "AeroDriver Build Script"
print_color $BLUE "======================"

# Check if dotnet CLI is installed
if ! command -v dotnet &> /dev/null; then
    print_color $RED "Error: .NET CLI is not installed or not in PATH"
    exit 1
fi

# Show dotnet info
print_color $YELLOW "Using .NET:"
dotnet --version

# Clean if requested
if [ "$CLEAN" = true ]; then
    print_color $YELLOW "Cleaning solution..."
    dotnet clean --configuration "$CONFIGURATION" --verbosity minimal
fi

# Restore packages
print_color $YELLOW "Restoring packages..."
dotnet restore --verbosity minimal

# Build
print_color $YELLOW "Building solution ($CONFIGURATION)..."
if ! dotnet build --configuration "$CONFIGURATION" --no-restore --verbosity minimal; then
    print_color $RED "Build failed"
    exit 1
fi

# Run tests if requested
if [ "$TEST" = true ]; then
    print_color $YELLOW "Running tests..."
    if ! dotnet test --configuration "$CONFIGURATION" --no-build --verbosity minimal --logger "console;verbosity=minimal"; then
        print_color $RED "Tests failed"
        exit 1
    fi
fi

# Create packages if requested
if [ "$PACK" = true ]; then
    print_color $YELLOW "Creating packages..."
    if ! dotnet pack --configuration "$CONFIGURATION" --no-build --verbosity minimal; then
        print_color $RED "Pack failed"
        exit 1
    fi
fi

# Publish if requested
if [ "$PUBLISH" = true ]; then
    print_color $YELLOW "Publishing application for $RUNTIME..."
    if ! dotnet publish src/AeroDriver.CLI/AeroDriver.CLI.csproj --configuration "$CONFIGURATION" --runtime "$RUNTIME" --self-contained false --verbosity minimal; then
        print_color $RED "Publish failed"
        exit 1
    fi
    print_color $GREEN "Published to: src/AeroDriver.CLI/bin/$CONFIGURATION/net8.0/$RUNTIME/publish/"
fi

# Run analysis if requested
if [ "$ANALYZE" = true ]; then
    print_color $YELLOW "Running code analysis..."
    
    # Run format check
    if command -v dotnet format &> /dev/null; then
        if ! dotnet format --verify-no-changes --verbosity minimal; then
            print_color $YELLOW "Code formatting issues detected. Run 'dotnet format' to fix."
        fi
    fi
fi

print_color $GREEN "Build completed successfully!"