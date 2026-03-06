# Gateway MVP Plumbing Contracts

Interface definitions for the gateway MVP plumbing layer.

## Contract Documents

| Contract | Purpose |
|----------|---------|
| [interfaces.md](interfaces.md) | Public interfaces and DI registration contracts |

## Contract Principles

- All new gateway types follow the MsClaw.Core pattern: public interface alongside implementation
- DI registration uses interface → concrete type mapping
- Configuration binding uses the Options pattern (`IOptions<GatewayOptions>`)
- The gateway consumes MsClaw.Core interfaces, never concrete types directly
