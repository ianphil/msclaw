# Tool Bridge Contracts

Interface definitions for the Tool Bridge provider abstraction and registry.

## Contract Documents

| Contract | Purpose |
|----------|---------|
| [interfaces.md](interfaces.md) | Core interface and type definitions |

## Contract Principles

- Every public type has a corresponding interface
- Interfaces are defined alongside implementations in `Services/Tools/`
- `AIFunction` from `Microsoft.Extensions.AI` is the tool unit — no custom wrappers
- `ToolDescriptor` is the only bridge-specific metadata type
- Read/write separation: `IToolCatalog` (consumers) vs `IToolRegistrar` (hosting layer)
- Session awareness isolated to `IToolExpander`
