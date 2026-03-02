# MsClaw Documentation

A [GitHub Copilot Extension](https://docs.github.com/en/copilot/building-copilot-extensions) that gives your AI agent a persistent identity — a **mind**.

## Getting Started

```bash
dotnet tool install -g MsClaw
msclaw --mind /path/to/your/mind
```

Or from source:

```bash
git clone https://github.com/ianphil/msclaw.git
cd msclaw && dotnet build
dotnet run --project src/MsClaw -- --mind /path/to/your/mind
```

## Guides

- [MsClaw Walkthrough](msclaw-walkthrough.md) — End-to-end overview of how MsClaw works
- [Extension Developer Guide](extension-developer-guide.md) — Build plugins for MsClaw
- [Bootstrap Flow (Existing Mind)](bootstrap-mind-flow.md) — What happens when you load a mind
- [Bootstrap Flow (New Mind)](bootstrap-new-mind-flow.md) — What happens when you scaffold a new mind

## Links

- [GitHub Repository](https://github.com/ianphil/msclaw)
- [NuGet Package](https://www.nuget.org/packages/MsClaw)
