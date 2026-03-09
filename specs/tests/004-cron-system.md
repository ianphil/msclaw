---
target:
  - src/MsClaw.Gateway/Services/Cron/CronJob.cs
  - src/MsClaw.Gateway/Services/Cron/CronJobStore.cs
  - src/MsClaw.Gateway/Services/Cron/ICronJobStore.cs
  - src/MsClaw.Gateway/Services/Cron/ICronRunHistoryStore.cs
  - src/MsClaw.Gateway/Services/Cron/ICronEngine.cs
  - src/MsClaw.Gateway/Services/Cron/CronEngine.cs
  - src/MsClaw.Gateway/Services/Cron/ICronJobExecutor.cs
  - src/MsClaw.Gateway/Services/Cron/PromptJobExecutor.cs
  - src/MsClaw.Gateway/Services/Cron/CommandJobExecutor.cs
  - src/MsClaw.Gateway/Services/Cron/CronToolProvider.cs
  - src/MsClaw.Gateway/Services/Cron/CronRunResult.cs
  - src/MsClaw.Gateway/Services/Cron/CronRunHistory.cs
  - src/MsClaw.Gateway/Services/Cron/ICronErrorClassifier.cs
  - src/MsClaw.Gateway/Services/Cron/DefaultCronErrorClassifier.cs
  - src/MsClaw.Gateway/Services/Cron/ICronOutputSink.cs
  - src/MsClaw.Gateway/Services/Cron/SignalRCronOutputSink.cs
  - src/MsClaw.Gateway/Services/Cron/CronScheduleCalculator.cs
  - src/MsClaw.Gateway/Services/Cron/CronStaggerCalculator.cs
  - src/MsClaw.Gateway/Extensions/GatewayServiceExtensions.cs
---

# Cron System Spec Tests

Validates that the cron system provides scheduled agent autonomy through job persistence, timer-based evaluation, isolated execution, and tool-based management.

## Job Model

### CronJob record has required fields for scheduling

Operators need reliable job definitions that capture all scheduling metadata. If the job model is missing fields like schedule, payload, or status, jobs cannot be persisted, evaluated, or managed.

```
Given the src/MsClaw.Gateway/Services/Cron/CronJob.cs file
When examining the CronJob record
Then it has an id field of type string
And it has a name field of type string
And it has a schedule field of a polymorphic schedule type
And it has a payload field of a polymorphic payload type
And it has a status field of an enum type with Enabled and Disabled values (Running is tracked in-memory by the engine, not persisted)
And it has createdAtUtc, lastRunAtUtc, and nextRunAtUtc fields for temporal tracking
```

### JobSchedule supports three schedule variants

Operators need one-shot, interval, and cron expression schedules. If any variant is missing, the agent cannot translate natural language schedule requests into job definitions.

```
Given the src/MsClaw.Gateway/Services/Cron/CronJob.cs file
When examining the JobSchedule type hierarchy
Then there is a OneShot variant with a fireAtUtc field
And there is a FixedInterval variant with an intervalMs field
And there is a CronExpression variant with expression and timezone fields
And the type uses polymorphic JSON serialization with a type discriminator
```

### JobPayload supports prompt and command variants

Operators need both LLM-backed and deterministic execution modes. If PromptPayload is missing, scheduled reasoning tasks are impossible. If CommandPayload is missing, scripted tasks waste token budget on unnecessary LLM sessions.

```
Given the src/MsClaw.Gateway/Services/Cron/CronJob.cs file
When examining the JobPayload type hierarchy
Then there is a PromptPayload variant with a prompt field and optional preloadToolNames
And there is a CommandPayload variant with a command field and optional arguments and timeoutSeconds
And the type uses polymorphic JSON serialization with a type discriminator
```

## Job Persistence

### CronJobStore persists jobs to the msclaw cron directory

Operators expect jobs to survive gateway restarts. If jobs are only in memory, every restart loses all scheduled work.

```
Given the src/MsClaw.Gateway/Services/Cron/CronJobStore.cs file
When examining the class implementation
Then it reads and writes job data to a file within the ~/.msclaw/cron/ directory
And it uses atomic write (write to temp then rename) to prevent corruption
And it maintains an in-memory cache and flushes to disk on every mutation
And it supports adding, updating, removing, and getting jobs
```

### CronJobStore implements ICronJobStore interface

The engine and tool provider must access the store through an interface for testability. If the store is a concrete dependency, unit tests require filesystem access.

```
Given the src/MsClaw.Gateway/Services/Cron/CronJobStore.cs file
When examining the class declaration
Then it implements an ICronJobStore interface
And the ICronJobStore interface defines methods for InitializeAsync, GetAllJobsAsync, GetJobAsync, AddJobAsync, UpdateJobAsync, and RemoveJobAsync
And it also implements ICronRunHistoryStore as a separate interface
```

### CronJobStore tracks run history with pruning via ICronRunHistoryStore

Operators need execution history for debugging. Without history pruning, disk usage grows unbounded. History is on a separate interface from job CRUD per Interface Segregation Principle.

```
Given the src/MsClaw.Gateway/Services/Cron/CronJobStore.cs file
When examining the run history methods
Then it implements ICronRunHistoryStore which defines AppendRunRecordAsync and GetRunHistoryAsync
And it appends run records to per-job history files
And it provides a method to retrieve run history for a given job ID
And it enforces size or line count limits to prevent unbounded growth
```

## Execution

### ICronJobExecutor defines the executor contract

The engine must dispatch jobs to different executors based on payload type. Without a common abstraction, the engine would need payload-specific dispatch logic.

```
Given the src/MsClaw.Gateway/Services/Cron/ICronJobExecutor.cs file
When examining the interface definition
Then it defines a PayloadType property that returns the Type the executor handles
And it defines an ExecuteAsync method that accepts a CronJob, a run ID string, and a CancellationToken
And ExecuteAsync returns a Task of CronRunResult
```

### PromptJobExecutor creates isolated sessions

Each prompt job must run in its own session with no prior history. If sessions are shared, conversation context from previous runs leaks into new executions.

```
Given the src/MsClaw.Gateway/Services/Cron/PromptJobExecutor.cs file
When examining the class implementation
Then it implements ICronJobExecutor with PayloadType set to PromptPayload
And it creates or gets a session using a cron-prefixed key that includes the job ID and run ID
And it sends the job's prompt to the session
And it returns a CronRunResult with content from the session response
```

### CommandJobExecutor runs shell commands

Deterministic tasks like scripts and backups should execute without LLM overhead. If CommandPayload routes through an LLM session, every scheduled script wastes tokens.

```
Given the src/MsClaw.Gateway/Services/Cron/CommandJobExecutor.cs file
When examining the class implementation
Then it implements ICronJobExecutor with PayloadType set to CommandPayload
And it starts a process using the command from the payload
And it captures stdout and stderr from the process
And it enforces a timeout and returns a CronRunResult
```

### CronRunResult is payload-agnostic

The engine records history and publishes output identically regardless of how the job ran. If result types differ per executor, the engine needs branching logic for each payload type.

```
Given the src/MsClaw.Gateway/Services/Cron/CronRunResult.cs file
When examining the record definition
Then it has a Content field for the output text
And it has an Outcome field distinguishing success from failure
And it has an optional ErrorMessage field
And it has a DurationMs field for execution timing
```

## Engine

### CronEngine is a hosted service with timer-based evaluation

The gateway must evaluate due jobs automatically without human prompts. If there is no timer loop, jobs only fire when someone asks.

```
Given the src/MsClaw.Gateway/Services/Cron/CronEngine.cs file
When examining the class declaration
Then it implements IHostedService
And it creates a periodic timer in StartAsync
And it has a tick method that loads jobs, evaluates which are due, and dispatches to executors
And StopAsync cancels the timer and waits for in-flight executions
```

### CronEngine resolves executors by payload type

Adding a new job type must require only a new executor, not engine changes. If the engine uses switch statements on payload type, every new payload requires engine modification.

```
Given the src/MsClaw.Gateway/Services/Cron/CronEngine.cs file
When examining the executor resolution logic
Then it accepts a collection of ICronJobExecutor instances
And it selects the executor whose PayloadType matches the job's payload type
```

## Error Classification

### CronErrorClassifier distinguishes transient from permanent errors

One-shot jobs must retry on transient errors but finalize on permanent ones. Without classification, all errors get the same treatment — either over-retrying permanent failures or abandoning recoverable ones.

```
Given the src/MsClaw.Gateway/Services/Cron/DefaultCronErrorClassifier.cs file
When examining the classification logic
Then it implements the ICronErrorClassifier interface
And it classifies network errors, timeouts, and server errors as transient
And it classifies authentication failures and configuration errors as permanent
And the interface defines a method that accepts an Exception and returns whether it is transient
```

## Tool Provider

### CronToolProvider implements IToolProvider with 7 tools

The agent needs cron management tools in every session. If tools aren't registered with the tool bridge, the agent cannot create, list, or manage jobs.

```
Given the src/MsClaw.Gateway/Services/Cron/CronToolProvider.cs file
When examining the class declaration
Then it implements IToolProvider
And its Tier is Bundled
And DiscoverAsync returns exactly 7 ToolDescriptor instances
And the tool names include cron_create, cron_list, cron_get, cron_update, cron_delete, cron_pause, and cron_resume
And all descriptors have AlwaysVisible set to true
```

## DI Registration

### Cron services are registered in GatewayServiceExtensions

If cron services aren't registered in DI, the engine won't start, tools won't be discovered, and executors won't be resolved.

```
Given the src/MsClaw.Gateway/Extensions/GatewayServiceExtensions.cs file
When examining the AddGatewayServices method
Then it registers CronJobStore as both ICronJobStore and ICronRunHistoryStore (same singleton instance)
And it registers CronEngine as both ICronEngine and IHostedService
And it registers CronToolProvider as IToolProvider
And it registers PromptJobExecutor and CommandJobExecutor as ICronJobExecutor
And it registers DefaultCronErrorClassifier as ICronErrorClassifier
And it registers SignalRCronOutputSink as ICronOutputSink
```
