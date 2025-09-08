# Contributing to AeroDriver

Thank you for your interest in contributing to AeroDriver! We welcome contributions from the community.

## Getting Started

### Prerequisites

- .NET 8.0 SDK or later
- Visual Studio 2022 or Visual Studio Code
- Git

### Setting up the Development Environment

1. Fork the repository
2. Clone your fork:
   ```bash
   git clone https://github.com/your-username/AeroDriver.git
   cd AeroDriver
   ```

3. Restore dependencies:
   ```bash
   dotnet restore
   ```

4. Build the solution:
   ```bash
   dotnet build
   ```

5. Run tests:
   ```bash
   dotnet test
   ```

## Development Workflow

### Building the Project

Use the provided build scripts:

**Windows (PowerShell):**
```powershell
./build.ps1 -Configuration Release -Test
```

**Linux/macOS:**
```bash
./build.sh --configuration Release --test
```

### Running the Application

**CLI Application:**
```bash
dotnet run --project src/AeroDriver.CLI
```

**Available Commands:**
- `auto` - Auto mode (recommended)
- `scan` - Scan for driver updates
- `list` - List all drivers
- `info` - Show system information
- `help` - Show help

## Code Style Guidelines

### General Guidelines

- Follow .NET coding conventions
- Use meaningful variable and method names
- Write unit tests for new functionality
- Keep methods focused and small
- Use async/await for I/O operations

### Code Formatting

The project uses `.editorconfig` for consistent formatting. Most IDEs will automatically apply these rules.

To check formatting:
```bash
dotnet format --verify-no-changes
```

To fix formatting:
```bash
dotnet format
```

### Naming Conventions

- **Classes:** PascalCase (`DriverService`, `BackupInfo`)
- **Methods:** PascalCase (`GetDriversAsync`, `CreateBackupAsync`)
- **Variables:** camelCase (`driverInfo`, `backupPath`)
- **Constants:** PascalCase (`MaxBackupGenerations`)
- **Private fields:** camelCase with underscore prefix (`_whqlService`)

## Testing

### Running Tests

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test tests/AeroDriver.Core.Tests
```

### Writing Tests

- Use xUnit framework
- Follow Arrange-Act-Assert pattern
- Use descriptive test method names
- Mock external dependencies using Moq
- Aim for high test coverage

Example test:
```csharp
[Fact]
public async Task GetDriversAsync_ReturnsDriverList()
{
    // Arrange
    var service = new DriverService(_mockWhqlService.Object, _mockBackupService.Object);

    // Act
    var result = await service.GetDriversAsync();

    // Assert
    Assert.NotNull(result);
    Assert.IsType<List<DriverInfo>>(result);
}
```

## Submitting Changes

### Pull Request Process

1. Create a feature branch from main:
   ```bash
   git checkout -b feature/your-feature-name
   ```

2. Make your changes and commit them:
   ```bash
   git add .
   git commit -m "Add meaningful commit message"
   ```

3. Push to your fork:
   ```bash
   git push origin feature/your-feature-name
   ```

4. Create a Pull Request on GitHub

### Commit Message Guidelines

- Use the present tense ("Add feature" not "Added feature")
- Use the imperative mood ("Move cursor to..." not "Moves cursor to...")
- Limit the first line to 72 characters or less
- Reference issues and pull requests liberally after the first line

Good examples:
- `Add driver version comparison logic`
- `Fix backup service null reference exception`
- `Update README with new installation instructions`

### Pull Request Requirements

Before submitting a pull request, ensure:

- [ ] All tests pass
- [ ] Code follows style guidelines
- [ ] New code has appropriate test coverage
- [ ] Documentation is updated if needed
- [ ] No merge conflicts with main branch

## Reporting Issues

### Bug Reports

When reporting bugs, include:

- Operating system and version
- .NET version
- Steps to reproduce
- Expected vs actual behavior
- Error messages or logs

### Feature Requests

When requesting features:

- Explain the use case
- Describe the proposed solution
- Consider implementation complexity
- Check if similar features exist

## Architecture Overview

### Project Structure

```
src/
├── AeroDriver.Core/          # Core business logic
│   ├── Models/              # Data models
│   ├── Services/            # Business services
│   └── Interfaces/          # Service interfaces
├── AeroDriver.CLI/          # Command-line interface
├── AeroDriver.UI/           # User interface
└── AeroDriver.Languages/    # Localization resources

tests/
├── AeroDriver.Core.Tests/   # Core unit tests
└── AeroDriver.UI.Tests/     # UI tests
```

### Key Components

- **DriverService:** Main service for driver operations
- **WhqlDatabaseService:** WHQL driver database integration
- **BackupService:** Driver backup and restore functionality
- **SettingsService:** Application settings management

## License

By contributing to AeroDriver, you agree that your contributions will be licensed under the MIT License.