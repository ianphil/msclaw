# Cron System Contracts

Interface definitions for the cron system.

## Contract Documents

| Contract | Purpose |
|----------|---------|
| [interfaces.md](interfaces.md) | Core interfaces: `ICronEngine`, `ICronJobStore`, `ICronJobExecutor` |

## Contract Principles

- Every public type has a corresponding interface for testability
- Interfaces live alongside their implementations in `Services/Cron/`
- `ICronJobExecutor` implementations are resolved from DI by payload type
- `CronToolProvider` delegates to `ICronEngine` and `ICronJobStore` — no direct persistence access
