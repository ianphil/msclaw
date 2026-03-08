namespace MsClaw.Core;

/// <summary>
/// Canonical path and filename constants for the mind directory structure.
/// Single source of truth used by scaffold, validation, and identity loading.
/// </summary>
internal static class MindPaths
{
    // Root templates
    public const string SoulFile = "SOUL.md";
    public const string BootstrapFile = "bootstrap.md";

    // Working memory
    public const string WorkingMemoryDir = ".working-memory";
    public const string MemoryFile = "memory.md";
    public const string RulesFile = "rules.md";
    public const string LogFile = "log.md";

    // GitHub structure
    public const string GitHubDir = ".github";
    public const string CopilotInstructionsFile = "copilot-instructions.md";
    public const string AgentsDir = "agents";
    public const string SkillsDir = "skills";
    public const string AgentFilePattern = "*.agent.md";

    // IDEA directories
    public const string DomainsDir = "domains";
    public const string InitiativesDir = "initiatives";
    public const string ExpertiseDir = "expertise";
    public const string InboxDir = "inbox";
    public const string ArchiveDir = "archive";
}
