# AI Notes — Log

## 2026-03-06
- architecture: feature 002 planning keeps Copilot SDK data types flowing through directly while holding service lifecycles behind IGatewayClient and IGatewaySession boundaries.
- planning: feature 002 artifacts currently disagree on hub boundaries, with plan/contracts using split IConcurrencyGate plus ISessionMap while the spec test draft still expects a direct CopilotClient and ICallerRegistry shape.
