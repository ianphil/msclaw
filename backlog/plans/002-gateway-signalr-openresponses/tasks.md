# Gateway SignalR + OpenResponses Tasks (TDD)

## TDD Approach

All implementation follows strict Red-Green-Refactor:
1. **RED**: Write failing test first
2. **GREEN**: Write minimal code to pass test
3. **REFACTOR**: Clean up while keeping tests green

### Two Test Layers
| Layer | Purpose | When to Run |
|-------|---------|-------------|
| **Unit Tests** | Implementation TDD (Red-Green-Refactor) | During implementation |
| **Spec Tests** | Intent-based acceptance validation | After all phases complete |

### Design Principle
> **Never Rewrite What You've Already Imported.**
> The Copilot SDK provides `CopilotClient`, `CopilotSession`, `SessionEvent`, and all event subtypes.
> SDK **data types** flow through untransformed. SDK **service types** stay behind testable
> interface boundaries (`IGatewayClient`, `IGatewaySession`) — that's the Dependency Rule, not a rewrite.
> We add only what the SDK lacks: caller-key mapping (`ISessionMap`), concurrency gating
> (`IConcurrencyGate`), push-to-pull bridging (`SessionEventBridge`), and orchestration (`AgentMessageService`).

## User Story Mapping

| Story | spec.md Reference | Spec Tests |
|-------|-------------------|------------|
| Operator (Web UI) | FR-2, FR-5 | SignalR Hub Streaming, Chat UI |
| Operator (Session Mgmt) | FR-2.3-2.6 | Session Management |
| Automation Client (HTTP) | FR-3 | OpenResponses Endpoint |
| Infrastructure Operator | FR-4 | Health Probes |
| Mind Author | FR-1.3 | Agent Runtime Identity |

## Dependencies

```
Phase 1 (Coordination) ──► Phase 2 (Hub) ──► Phase 5 (Chat UI)
      │                                             │
      └──► Phase 3 (Health) ──► Phase 6 (Integration Tests)
      │                              ▲
      └──► Phase 4 (OpenResponses) ──┘
```

## Phase 1: Coordination Layer

### IConcurrencyGate — Concurrency Gate
- [x] T001 [TEST] Write test: TryAcquire returns true when no active run for caller
- [x] T002 [TEST] Write test: TryAcquire returns false when caller already has active run
- [x] T003 [TEST] Write test: Release followed by TryAcquire returns true (slot freed)
- [x] T004 [TEST] Write test: TryAcquire for different callers succeeds independently
- [x] T005 [IMPL] Define IConcurrencyGate interface and implement in CallerRegistry with ConcurrentDictionary<string, SemaphoreSlim>

### ISessionMap — Caller-to-Session Mapping
- [x] T006 [TEST] Write test: SetSessionId + GetSessionId round-trips correctly
- [x] T007 [TEST] Write test: GetSessionId returns null for unknown caller
- [x] T008 [TEST] Write test: ListCallers returns all registered caller-session pairs
- [x] T009 [IMPL] Define ISessionMap interface and implement in CallerRegistry with ConcurrentDictionary<string, string>

### IGatewayClient — Session Operations Boundary
- [x] T010 [TEST] Write test: CreateSessionAsync returns IGatewaySession with valid SessionId
- [x] T011 [TEST] Write test: ResumeSessionAsync returns IGatewaySession for known session ID
- [x] T012 [TEST] Write test: ListSessionsAsync returns metadata for created sessions
- [x] T013 [IMPL] Extend IGatewayClient interface with CreateSessionAsync, ResumeSessionAsync, ListSessionsAsync, DeleteSessionAsync
- [x] T014 [IMPL] Define IGatewaySession interface (SessionId, On, SendAsync, AbortAsync, GetMessagesAsync)
- [x] T015 [IMPL] Implement CopilotGatewayClient session methods (delegate to SDK) and CopilotGatewaySession wrapper

### GatewayHostedService — Expose System Message
- [x] T016 [TEST] Write test: GatewayHostedService exposes loaded system message string after StartAsync
- [x] T017 [IMPL] Modify GatewayHostedService to capture LoadSystemMessageAsync return value and expose SystemMessage property

### DI Wiring
- [x] T018 [TEST] Write test: IConcurrencyGate is registered as singleton in DI
- [x] T019 [TEST] Write test: ISessionMap is registered as singleton in DI (same CallerRegistry instance)
- [x] T020 [IMPL] Register CallerRegistry, IConcurrencyGate, ISessionMap, and AgentMessageService in StartCommand.ConfigureServices

## Phase 2: SignalR Hub

### SessionEventBridge
- [x] T021 [TEST] Write test: Bridge yields SDK events written by push callback as IAsyncEnumerable
- [x] T022 [TEST] Write test: Bridge completes enumerable when SessionIdleEvent fires
- [x] T023 [TEST] Write test: Bridge completes enumerable when CancellationToken is cancelled
- [x] T024 [IMPL] Implement SessionEventBridge using Channel<SessionEvent> (push callback writes, reader yields)

### AgentMessageService
- [x] T025 [TEST] Write test: SendAsync acquires IConcurrencyGate and releases after stream completes
- [x] T026 [TEST] Write test: SendAsync throws when IConcurrencyGate.TryAcquire returns false
- [x] T027 [TEST] Write test: SendAsync creates new session via IGatewayClient when no session exists for caller
- [x] T028 [TEST] Write test: SendAsync resumes existing session via IGatewayClient when session exists for caller
- [x] T029 [IMPL] Implement AgentMessageService orchestrating gate → session → bridge → yield SDK events → release

### IGatewayHubClient
- [x] T030 [IMPL] Define IGatewayHubClient interface (ReceiveEvent, ReceivePresence)

### GatewayHub — Thin Routing Layer
- [x] T031 [TEST] Write test: SendMessage delegates to AgentMessageService.SendAsync with Context.ConnectionId as caller key
- [x] T032 [IMPL] Implement GatewayHub extending Hub<IGatewayHubClient>, injecting AgentMessageService + IGatewayClient + ISessionMap
- [x] T033 [TEST] Write test: CreateSession delegates to IGatewayClient.CreateSessionAsync and returns session ID
- [x] T034 [IMPL] Implement CreateSession hub method
- [x] T035 [TEST] Write test: ListSessions delegates to IGatewayClient.ListSessionsAsync
- [x] T036 [IMPL] Implement ListSessions hub method
- [x] T037 [TEST] Write test: GetHistory resolves caller's session from ISessionMap and delegates to IGatewaySession.GetMessagesAsync
- [x] T038 [IMPL] Implement GetHistory hub method
- [x] T039 [TEST] Write test: AbortResponse resolves caller's session and delegates to IGatewaySession.AbortAsync
- [x] T040 [IMPL] Implement AbortResponse hub method

## Phase 3: Health Probes

### Liveness Endpoint
- [x] T041 [TEST] Write test: GET /health returns 200 with { status: "Healthy" } always
- [x] T042 [IMPL] Add GET /health endpoint to MapEndpoints

### Readiness Endpoint
- [x] T043 [TEST] Write test: GET /health/ready returns 200 when hosted service is ready
- [x] T044 [TEST] Write test: GET /health/ready returns 503 with component detail when not ready
- [x] T045 [IMPL] Add GET /health/ready endpoint to MapEndpoints

### Remove Legacy /healthz
- [x] T046 [TEST] Write test: /healthz no longer exists (or redirects)
- [x] T047 [IMPL] Replace /healthz with /health in MapEndpoints

## Phase 4: OpenResponses Library

### Project Scaffold
- [ ] T048 [IMPL] Create MsClaw.OpenResponses project (class library, net10.0)
- [ ] T049 [IMPL] Create MsClaw.OpenResponses.Tests project (xUnit, net10.0)
- [ ] T050 [IMPL] Add both projects to MsClaw.slnx

### Request DTO
- [ ] T051 [TEST] Write test: ResponseRequest deserializes from valid JSON with model, input, stream fields
- [ ] T052 [TEST] Write test: ResponseRequest validation rejects missing model
- [ ] T053 [TEST] Write test: ResponseRequest validation rejects empty input
- [ ] T054 [IMPL] Implement ResponseRequest DTO with validation

### Response DTOs (genuinely new — no SDK equivalent)
- [ ] T055 [TEST] Write test: ResponseObject serializes to expected OpenResponses JSON structure
- [ ] T056 [TEST] Write test: OutputItem and ContentPart serialize correctly
- [ ] T057 [IMPL] Implement ResponseObject, OutputItem, ContentPart DTOs

### SSE Formatting (SDK event → OpenResponses SSE mapping)
- [ ] T058 [TEST] Write test: Maps AssistantMessageDeltaEvent → response.output_text.delta SSE event
- [ ] T059 [TEST] Write test: Maps AssistantMessageEvent → response.output_text.done + response.completed
- [ ] T060 [TEST] Write test: Maps SessionIdleEvent → terminal [DONE] marker
- [ ] T061 [TEST] Write test: Maps SessionErrorEvent → response.failed
- [ ] T062 [IMPL] Implement SDK event → OpenResponses SSE formatter

### Error Responses
- [ ] T063 [TEST] Write test: Error response includes code, message, and request_id
- [ ] T064 [IMPL] Implement error response DTOs and helpers

### Middleware
- [ ] T065 [TEST] Write test: POST /v1/responses with stream:false returns complete ResponseObject
- [ ] T066 [TEST] Write test: POST /v1/responses with stream:true returns SSE event stream
- [ ] T067 [TEST] Write test: POST /v1/responses returns 409 when caller has active run
- [ ] T068 [TEST] Write test: POST /v1/responses returns 400 for malformed request
- [ ] T069 [IMPL] Implement OpenResponsesMiddleware using AgentMessageService

### Extension Method
- [ ] T070 [TEST] Write test: MapOpenResponses registers the /v1/responses endpoint
- [ ] T071 [IMPL] Implement EndpointRouteBuilderExtensions.MapOpenResponses()

### Gateway Integration
- [ ] T072 [IMPL] Add MsClaw.OpenResponses project reference to MsClaw.Gateway.csproj
- [ ] T073 [IMPL] Call MapOpenResponses() in StartCommand.MapEndpoints

## Phase 5: Chat UI

### Static Files Middleware
- [ ] T074 [TEST] Write test: StartCommand pipeline includes UseDefaultFiles and UseStaticFiles
- [ ] T075 [IMPL] Add UseDefaultFiles() + UseStaticFiles() to RunGatewayAsync

### Chat HTML
- [ ] T076 [IMPL] Create wwwroot/index.html with SignalR JS client connection to /gateway
- [ ] T077 [IMPL] Implement message send form and streamed response rendering

### Chat Styling
- [ ] T078 [IMPL] Create wwwroot/css/site.css with chat bubble styling

## Phase 6: Integration Tests

### Hub Streaming
- [ ] T079 [TEST] Write integration test: Connect to hub, send message, receive streamed SDK events
- [ ] T080 [IMPL] Implement test infrastructure (WebApplicationFactory or in-process gateway)

### Concurrency Rejection
- [ ] T081 [TEST] Write integration test: Second concurrent send from same caller is rejected

### OpenResponses HTTP
- [ ] T082 [TEST] Write integration test: POST /v1/responses returns valid OpenResponses JSON
- [ ] T083 [TEST] Write integration test: POST /v1/responses with stream:true returns SSE events

### Health Probes
- [ ] T084 [TEST] Write integration test: /health returns 200, /health/ready reflects hosted service state

## Final Validation

After all implementation phases are complete:

- [ ] `dotnet build src/MsClaw.slnx --nologo` passes
- [ ] `dotnet test src/MsClaw.Gateway.Tests/MsClaw.Gateway.Tests.csproj --nologo` passes
- [ ] `dotnet test src/MsClaw.OpenResponses.Tests/MsClaw.OpenResponses.Tests.csproj --nologo` passes
- [ ] `dotnet test src/MsClaw.Core.Tests/MsClaw.Core.Tests.csproj --nologo` passes (no regressions)
- [ ] Run spec tests with `/spec-tests` skill using `specs/tests/002-gateway-signalr-openresponses.md`
- [ ] All spec tests pass → feature complete

## Task Summary

| Phase | Tasks | [TEST] | [IMPL] |
|-------|-------|--------|--------|
| Phase 1: Coordination Layer | T001-T020 | 13 | 7 |
| Phase 2: SignalR Hub | T021-T040 | 12 | 8 |
| Phase 3: Health Probes | T041-T047 | 4 | 3 |
| Phase 4: OpenResponses | T048-T073 | 15 | 11 |
| Phase 5: Chat UI | T074-T078 | 1 | 4 |
| Phase 6: Integration Tests | T079-T084 | 5 | 1 |
| **Total** | **84** | **50** | **34** |
