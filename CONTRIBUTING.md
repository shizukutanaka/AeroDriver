# Contributing to AeroDriver

Thank you for your interest in contributing to AeroDriver! We appreciate your time and effort in helping us improve this project.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Workflow](#development-workflow)
- [Coding Guidelines](#coding-guidelines)
- [Pull Request Process](#pull-request-process)
- [Reporting Issues](#reporting-issues)
- [License](#license)

## Code of Conduct

This project and everyone participating in it is governed by our [Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code. Please report any unacceptable behavior to [contact@example.com](mailto:contact@example.com).

## Getting Started

### Prerequisites

- [.NET 7.0 SDK](https://dotnet.microsoft.com/download/dotnet/7.0) or later
- [Git](https://git-scm.com/)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) or [Visual Studio Code](https://code.visualstudio.com/)

### Setting Up the Development Environment

1. Fork the repository on GitHub
2. Clone your fork locally:
   ```bash
   git clone https://github.com/your-username/AeroDriver.git
   cd AeroDriver
   ```
3. Add the upstream repository:
   ```bash
   git remote add upstream https://github.com/shizukutanaka/AeroDriver.git
   ```
4. Restore the NuGet packages:
   ```bash
   dotnet restore
   ```
5. Build the solution:
   ```bash
   dotnet build
   ```

## Development Workflow

1. Create a new branch for your feature or bugfix:
   ```bash
   git checkout -b feature/your-feature-name
   # or
   git checkout -b bugfix/issue-number-short-description
   ```

2. Make your changes following the coding guidelines below

3. Run tests:
   ```bash
   dotnet test
   ```

4. Commit your changes with a descriptive commit message:
   ```bash
   git commit -m "Add feature: short description of changes"
   ```

5. Push your changes to your fork:
   ```bash
   git push origin your-branch-name
   ```

6. Create a Pull Request (PR) to the main repository's `main` branch

## Coding Guidelines

### General

- Follow the [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use meaningful variable, method, and class names
- Keep methods small and focused on a single responsibility
- Add XML documentation for all public types and members
- Write unit tests for new features and bug fixes

### Code Style

- Use 4 spaces for indentation (no tabs)
- Use `camelCase` for private fields and local variables
- Use `PascalCase` for method names, property names, class names, and namespaces
- Prefix interface names with "I" (e.g., `IDriverService`)
- Use `_camelCase` for private fields (with underscore prefix)
- Use `readonly` for fields that are only set in the constructor
- Use `var` when the type is obvious from the right side of the assignment

### Commit Messages

- Use the present tense ("Add feature" not "Added feature")
- Use the imperative mood ("Move cursor to..." not "Moves cursor to...")
- Limit the first line to 72 characters or less
- Reference issues and pull requests liberally
- Consider starting the commit message with an applicable emoji:
  - 🎨 `:art:` when improving the format/structure of the code
  - 🐛 `:bug:` when fixing a bug
  - 🔥 `:fire:` when removing code or files
  - 📝 `:memo:` when writing docs
  - 🚀 `:rocket:` when improving performance
  - ✅ `:white_check_mark:` when adding tests
  - 🔒 `:lock:` when dealing with security
  - ⬆️ `:arrow_up:` when upgrading dependencies
  - ⬇️ `:arrow_down:` when downgrading dependencies

## Pull Request Process

1. Ensure any install or build dependencies are removed before the end of the layer when doing a build.
2. Update the README.md with details of changes to the interface, this includes new environment variables, exposed ports, useful file locations, and container parameters.
3. Increase the version numbers in any examples files and the README.md to the new version that this Pull Request would represent. The versioning scheme we use is [SemVer](http://semver.org/).
4. You may merge the Pull Request in once you have the sign-off of two other developers, or if you do not have permission to do that, you may request the second reviewer to merge it for you.

## Reporting Issues

When reporting issues, please include the following information:

- A clear and descriptive title
- Steps to reproduce the issue
- Expected behavior
- Actual behavior
- Screenshots if applicable
- Your operating system and version
- Any other relevant information

## License

By contributing to AeroDriver, you agree that your contributions will be licensed under its [MIT License](LICENSE).
