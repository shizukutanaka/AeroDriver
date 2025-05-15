# Contributing to AeroDriver

Thank you for considering contributing to AeroDriver! We welcome contributions from the community.

## How to Contribute

### 1. Report Issues
- Use the [Issues](https://github.com/shizukutanaka/AeroDriver/issues) tab to report bugs
- Search existing issues before creating a new one
- Provide as much detail as possible including:
  - Operating system
  - .NET version
  - Steps to reproduce
  - Expected vs actual behavior

### 2. Suggest Features
- Open an issue with the "enhancement" label
- Describe the feature and its benefits
- Explain why it would be useful for other users

### 3. Submit Code Changes

#### Development Setup
1. Fork the repository
2. Clone your fork locally:
   ```bash
   
   ```
3. Create a feature branch:
   ```bash
   git checkout -b feature/your-feature-name
   ```
4. Make your changes
5. Test your changes thoroughly
6. Commit with a descriptive message:
   ```bash
   git commit -m "Add feature: description of your changes"
   ```
7. Push to your fork:
   ```bash
   git push origin feature/your-feature-name
   ```
8. Create a Pull Request

#### Code Guidelines
- Follow C# naming conventions
- Add XML documentation for public methods
- Include proper error handling
- Write unit tests for new functionality
- Keep methods small and focused

#### Commit Message Format
```
type: short description

Longer description if needed
```

Types:
- `feat`: New features
- `fix`: Bug fixes
- `docs`: Documentation changes
- `style`: Code style changes (formatting, etc.)
- `refactor`: Code refactoring
- `test`: Adding or updating tests
- `chore`: Maintenance tasks

### 4. Code of Conduct
- Be respectful and constructive
- Help create a welcoming environment
- Focus on what's best for the community

## Development Notes

### Architecture
- Uses dependency injection for loose coupling
- Background services for automatic operations
- JSON file storage for simplicity
- Modular design for easy extension

### Testing
- Test your changes thoroughly
- Consider edge cases and error scenarios
- Ensure existing functionality still works

### Documentation
- Update README.md if needed
- Add code comments for complex logic
- Update CHANGELOG.md for significant changes

## Questions?

Feel free to open an issue for any questions about contributing.

Thank you for helping make AeroDriver better!