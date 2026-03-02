# Contributing to MsClaw

Thanks for your interest in contributing! Here's how to get started.

## Getting Started

1. Fork the repository
2. Clone your fork: `git clone https://github.com/<your-username>/msclaw.git`
3. Create a branch: `git checkout -b feature/your-feature`
4. Make your changes
5. Run the tests: `dotnet test`
6. Commit and push
7. Open a Pull Request

## Development Setup

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Any editor (VS Code recommended)

```bash
dotnet build
dotnet test
```

## Extension Development

MsClaw now supports extensions that register:
- tools
- hooks
- services
- commands
- HTTP routes

When contributing to extension runtime behavior:

1. Keep extension contracts and runtime wiring in `src/MsClaw/Core/`
2. Add/extend unit tests in `tests/MsClaw.Tests/Core/`
3. Run `dotnet test` before pushing
4. Run the manual smoke test in `.aidocs/e2e-extension-test.md` when behavior changes affect extension loading/lifecycle

For a working external sample extension, see:
- `https://github.com/ipdelete/hello-world-extension`

## Code Style

- Follow existing conventions in the codebase
- Use `nullable enable` — no suppressing nullability warnings without justification
- Keep classes focused and small
- Interfaces live in `Core/` alongside their implementations
- Models live in `Models/`

## Pull Requests

- Keep PRs focused on a single change
- Include tests for new functionality
- Ensure all existing tests pass before submitting
- If extension runtime behavior changes, include manual validation notes (commands/output) from the e2e extension test flow
- Use conventional commit messages: `feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`

## Reporting Issues

Open an issue with:
- A clear description of the problem
- Steps to reproduce
- Expected vs actual behavior
- .NET version and OS

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating, you agree to uphold it.
