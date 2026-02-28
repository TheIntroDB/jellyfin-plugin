# Contributing to TheIntroDB

First of all, thank you for your interest in contributing to TheIntroDB! We appreciate your help in making this project better.

## Getting Started

### Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) (version 9.0 or later recommended)
- A C# IDE (e.g., [Visual Studio](https://visualstudio.microsoft.com/), [JetBrains Rider](https://www.jetbrains.com/rider/), or [Visual Studio Code](https://code.visualstudio.com/) with C# Dev Kit)

### Installation

1. Clone the repository:

   ```bash
   git clone https://github.com/theintrodb/jellyfin-plugin.git
   cd jellyfin-plugin
   ```

2. Restore dependencies:

   ```bash
   dotnet restore
   ```

3. Build the plugin:
   ```bash
   dotnet build
   ```

## Development Standards

To maintain a consistent codebase, we follow standard C# coding conventions and use `.editorconfig` for formatting rules.

### Formatting

The project includes an `.editorconfig` file to enforce consistent code style. Most IDEs will pick this up automatically. You can also format the code using the `dotnet format` command:

```bash
# Check if code is properly formatted
dotnet format --verify-no-changes

# Automatically fix formatting issues
dotnet format
```

### Building & Testing

Before submitting a pull request, ensure the project builds correctly and all tests pass.

```bash
# Build the solution
dotnet build

# Run unit tests (if any)
dotnet test
```

## Pull Request Guidelines

1. **Create a Branch**: Create a descriptive branch name for your changes (e.g., `feat/add-new-feature` or `fix/issue-description`).
2. **Make Changes**: Implement your changes and ensure they follow the project's coding standards.
3. **Verify Changes**: Before submitting, ensure the code builds and passes all tests:
   ```bash
   dotnet build
   dotnet test
   ```
4. **Submit a PR**: Provide a clear and concise description of your changes in the pull request. Reference any related issues if applicable.

## CI/CD

Our GitHub Actions will automatically run build and test checks on every pull request. If these checks fail, you will need to fix them locally and push the changes before your PR can be merged.

---

Happy coding!
