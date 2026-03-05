<#
.SYNOPSIS
    LLM-as-Judge spec test runner using GitHub Copilot CLI.

.DESCRIPTION
    Evaluates intent-based specification tests against target files using the Copilot CLI
    as the LLM judge. Each test has an intent (WHY it matters) and an assertion (WHAT must
    be true). The judge evaluates BOTH — passing the assertion while violating intent is a failure.

    This replaces traditional unit tests for requirements that need semantic understanding
    (e.g., "does the config use CoreConfig derivation?" vs "does string X appear in file Y?").

    PREREQUISITES
      - GitHub Copilot CLI installed and authenticated (copilot --version)
      - PowerShell 7+

    SPEC FILE FORMAT
      Spec files are markdown with YAML frontmatter declaring target file(s):

        ---
        target: src/auth.py                    # single target
        ---
        # Feature Name

        ## Section Name

        ### Test Name

        Intent paragraph explaining WHY this test matters — what breaks
        for users if this requirement is not met.

        ```
        Given the src/auth.py file
        When a user submits valid credentials
        Then they are redirected to the dashboard
        ```

      Multiple targets use array syntax in frontmatter:

        ---
        target:
          - src/api.py
          - src/api_test.py
        ---

    STRUCTURE RULES
      - H1 (#)   = spec file title (ignored by parser)
      - H2 (##)  = test group/section name
      - H3 (###) = individual test case name
      - Text between H3 and code fence = intent (required)
      - Code fence after intent = assertion (required)
      - One behavior per test — if it fails, you know exactly what broke

    EXIT CODES
      0 = all tests passed (skips are OK)
      1 = one or more tests failed
      2 = all tests were skipped (no passes, no failures)

    FILES
      judge_prompt.md    - LLM evaluation prompt template (must be alongside this script)
      .spec-tests-failures.json - Written on failure, used by -RerunFailed

.PARAMETER SpecPath
    Path to a spec test .md file or a directory containing .md files.
    When a directory is given, all *.md files (except judge_prompt.md) are run.

.PARAMETER Target
    Override the target file declared in frontmatter. Useful for testing a spec
    against a different implementation (e.g., a branch copy).

.PARAMETER Model
    Copilot model to use (default: claude-haiku-4.5).
    Available: claude-sonnet-4, claude-sonnet-4.5, claude-haiku-4.5, gpt-5.2-codex, etc.

.PARAMETER TestName
    Run only the test with this exact name (H3 header text). Case-sensitive.

.PARAMETER DryRun
    Parse spec files and output the intermediate representation as JSON.
    No LLM calls are made. Useful to verify spec files parse correctly.

.PARAMETER RerunFailed
    Re-run only tests that failed in the previous run (reads .spec-tests-failures.json).
    Mutually exclusive with -TestName.

.EXAMPLE
    # Run all spec tests in a directory
    .\Invoke-SpecTests.ps1 specs\tests\

.EXAMPLE
    # Run a single spec file
    .\Invoke-SpecTests.ps1 specs\tests\001-coreconfig-provider.md

.EXAMPLE
    # Run one specific test by name
    .\Invoke-SpecTests.ps1 specs\tests\001-coreconfig-provider.md -TestName "Supports Question-Mark Default Syntax"

.EXAMPLE
    # Dry-run to verify parsing (no LLM calls)
    .\Invoke-SpecTests.ps1 specs\tests\ -DryRun

.EXAMPLE
    # Re-run only previously failed tests
    .\Invoke-SpecTests.ps1 specs\tests\ -RerunFailed

.EXAMPLE
    # Use a different model
    .\Invoke-SpecTests.ps1 specs\tests\ -Model claude-sonnet-4.5

.EXAMPLE
    # Override target file
    .\Invoke-SpecTests.ps1 specs\tests\001-coreconfig-provider.md -Target common\AET.Common\Utilities\ReferenceResolvingConfigurationProvider.cs

.LINK
    https://dev.azure.com/msazuredev/Azure%20Encrypted%20Transport
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$SpecPath,

    [string]$Target,

    [string]$Model = "claude-haiku-4.5",

    [string]$TestName,

    [switch]$DryRun,

    [switch]$RerunFailed
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ============================================================================
# Constants
# ============================================================================
$script:JudgePromptFile = Join-Path $PSScriptRoot "judge_prompt.md"
$script:FailureFile = Join-Path (Get-Location) ".spec-tests-failures.json"
$script:RunTimeout = 180

# ANSI colors
$script:GREEN  = "`e[92m"
$script:RED    = "`e[91m"
$script:YELLOW = "`e[93m"
$script:CYAN   = "`e[96m"
$script:BOLD   = "`e[1m"
$script:RESET  = "`e[0m"

# ============================================================================
# Frontmatter Parser
# ============================================================================
function Parse-Frontmatter {
    param([string]$Content)

    $metadata = @{}
    $remaining = $Content

    if (-not $Content.StartsWith("---")) {
        return @{ Metadata = $metadata; Content = $remaining }
    }

    # Find closing ---
    $match = [regex]::Match($Content.Substring(3), '\n---\s*(?:\n|$)')
    if (-not $match.Success) {
        return @{ Metadata = $metadata; Content = $remaining }
    }

    $frontmatterText = $Content.Substring(3, $match.Index)
    $remaining = $Content.Substring(3 + $match.Index + $match.Length)

    # Simple YAML parsing for target field
    $currentKey = $null
    $currentList = @()

    foreach ($line in $frontmatterText.Trim().Split("`n")) {
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith("- ") -and $currentKey) {
            $currentList += $trimmed.Substring(2).Trim()
        }
        elseif ($trimmed -match '^([^:]+):(.*)$' -and -not $line.StartsWith(" ") -and -not $line.StartsWith("`t")) {
            if ($currentKey -and $currentList.Count -gt 0) {
                $metadata[$currentKey] = $currentList
                $currentList = @()
            }
            $key = $Matches[1].Trim()
            $value = $Matches[2].Trim()
            if ($value) {
                $metadata[$key] = $value
                $currentKey = $null
            }
            else {
                $currentKey = $key
            }
        }
    }
    if ($currentKey -and $currentList.Count -gt 0) {
        $metadata[$currentKey] = $currentList
    }

    return @{ Metadata = $metadata; Content = $remaining }
}

function Get-TargetsFromFrontmatter {
    param([string]$SpecFilePath)

    $content = Get-Content $SpecFilePath -Raw -Encoding UTF8
    $parsed = Parse-Frontmatter $content

    if (-not $parsed.Metadata.ContainsKey("target")) {
        Write-Error "[missing-target] No 'target:' field in frontmatter of $SpecFilePath"
        exit 1
    }

    $rawTarget = $parsed.Metadata["target"]
    $targets = if ($rawTarget -is [string]) { @($rawTarget) } else { @($rawTarget) }

    $paths = @()
    foreach ($t in $targets) {
        if (-not (Test-Path $t)) {
            Write-Error "Target file not found: $t (declared in $SpecFilePath)"
            exit 1
        }
        $paths += $t
    }
    return $paths
}

# ============================================================================
# Spec Parser
# ============================================================================
function Parse-SpecTests {
    param([string]$Content)

    $parsed = Parse-Frontmatter $Content
    $lines = $parsed.Content.Split("`n")
    $tests = @()
    $currentSection = ""
    $i = 0

    while ($i -lt $lines.Count) {
        $line = $lines[$i]

        # Track H2 sections
        if ($line.StartsWith("## ")) {
            $currentSection = $line.Substring(3).Trim()
            $i++
            continue
        }

        # Found H3 test case
        if ($line.StartsWith("### ")) {
            $testName = $line.Substring(4).Trim()
            $testLine = $i + 1
            $i++

            # Collect intent lines until code block
            $intentLines = @()
            $missingAssertion = $false
            while ($i -lt $lines.Count) {
                if ($lines[$i].StartsWith('```')) { break }
                if ($lines[$i].StartsWith("## ") -or $lines[$i].StartsWith("### ")) {
                    $missingAssertion = $true
                    break
                }
                if ($lines[$i].Trim()) {
                    $intentLines += $lines[$i]
                }
                $i++
            }

            $intent = ($intentLines -join "`n").Trim()

            # Collect code block (assertion)
            $assertionLines = @()
            if ($i -lt $lines.Count -and $lines[$i].StartsWith('```')) {
                $i++ # Skip opening ```
                while ($i -lt $lines.Count -and -not $lines[$i].StartsWith('```')) {
                    $assertionLines += $lines[$i]
                    $i++
                }
                $i++ # Skip closing ```
            }
            else {
                $missingAssertion = $true
            }

            $assertionBlock = ($assertionLines -join "`n").Trim()
            $missingIntent = -not $intent

            if ($assertionBlock -or $missingAssertion) {
                $tests += [PSCustomObject]@{
                    Name             = $testName
                    Section          = $currentSection
                    Intent           = $intent
                    AssertionBlock   = $assertionBlock
                    LineNumber       = $testLine
                    MissingIntent    = $missingIntent
                    MissingAssertion = $missingAssertion
                }
            }
            continue
        }
        $i++
    }
    return $tests
}

# ============================================================================
# JSON Extraction (4 strategies)
# ============================================================================
function Extract-JudgeJson {
    param([string]$Text)

    if (-not $Text -or -not $Text.Trim()) {
        throw "Empty response from LLM"
    }

    # Strategy 1: Pure JSON
    try {
        $obj = $Text.Trim() | ConvertFrom-Json -ErrorAction Stop
        if ($null -ne $obj.passed) { return $obj }
    } catch {}

    # Strategy 2: Code block
    if ($Text -match '```(?:json)?\s*\n?(.*?)\n?```') {
        try {
            $obj = $Matches[1].Trim() | ConvertFrom-Json -ErrorAction Stop
            if ($null -ne $obj.passed) { return $obj }
        } catch {}
    }

    # Strategy 3: Balanced braces
    $extracted = Extract-BalancedJson $Text
    if ($extracted) {
        try {
            $obj = $extracted | ConvertFrom-Json -ErrorAction Stop
            if ($null -ne $obj.passed) { return $obj }
        } catch {}
    }

    # Strategy 4: Lenient (strip prefixes)
    $cleaned = $Text.Trim()
    foreach ($prefix in @("Here's the JSON:", "Here is the JSON:", "Response:", "Output:")) {
        if ($cleaned.StartsWith($prefix)) {
            $cleaned = $cleaned.Substring($prefix.Length).Trim()
        }
    }
    $extracted = Extract-BalancedJson $cleaned
    if ($extracted) {
        try {
            $obj = $extracted | ConvertFrom-Json -ErrorAction Stop
            if ($null -ne $obj.passed) { return $obj }
        } catch {}
    }

    $preview = if ($Text.Length -gt 200) { $Text.Substring(0, 200) + "..." } else { $Text }
    throw "Failed to extract JSON from response: $preview"
}

function Extract-BalancedJson {
    param([string]$Text)

    $start = $Text.IndexOf('{')
    if ($start -lt 0) { return $null }

    $depth = 0
    $inString = $false
    $escapeNext = $false

    for ($i = $start; $i -lt $Text.Length; $i++) {
        $char = $Text[$i]

        if ($escapeNext) { $escapeNext = $false; continue }
        if ($char -eq '\') { $escapeNext = $true; continue }
        if ($char -eq '"') { $inString = -not $inString; continue }

        if (-not $inString) {
            if ($char -eq '{') { $depth++ }
            elseif ($char -eq '}') {
                $depth--
                if ($depth -eq 0) {
                    return $Text.Substring($start, $i - $start + 1)
                }
            }
        }
    }
    return $null
}

# ============================================================================
# Judge Prompt Renderer
# ============================================================================
function Render-JudgePrompt {
    param(
        [PSCustomObject]$Test,
        [string[]]$TargetPaths
    )

    if (-not (Test-Path $script:JudgePromptFile)) {
        throw "Judge prompt file not found: $($script:JudgePromptFile)"
    }

    $template = Get-Content $script:JudgePromptFile -Raw -Encoding UTF8
    $targetFiles = ($TargetPaths | ForEach-Object { "- $(Resolve-Path $_)" }) -join "`n"

    return $template `
        -replace [regex]::Escape('{{target_files}}'), $targetFiles `
        -replace [regex]::Escape('{{test_name}}'), $Test.Name `
        -replace [regex]::Escape('{{test_section}}'), $Test.Section `
        -replace [regex]::Escape('{{intent}}'), $Test.Intent `
        -replace [regex]::Escape('{{assertion_block}}'), $Test.AssertionBlock
}

# ============================================================================
# LLM Judge (Copilot CLI)
# ============================================================================
function Invoke-Judge {
    param(
        [PSCustomObject]$Test,
        [string[]]$TargetPaths
    )

    # Skip: missing assertion
    if ($Test.MissingAssertion) {
        return [PSCustomObject]@{
            Passed    = $false
            Reasoning = "[missing-assertion] Test has no assertion code block."
            Error     = $null
            Skipped   = $true
        }
    }

    # Skip: missing intent
    if ($Test.MissingIntent) {
        return [PSCustomObject]@{
            Passed    = $false
            Reasoning = "[missing-intent] Test has no intent statement."
            Error     = $null
            Skipped   = $true
        }
    }

    $prompt = Render-JudgePrompt -Test $Test -TargetPaths $TargetPaths
    $maxRetries = 2
    $lastError = ""
    $responseText = ""

    for ($attempt = 0; $attempt -lt $maxRetries; $attempt++) {
        $currentPrompt = if ($attempt -eq 0) { $prompt } else { $prompt + "`n`nREMINDER: Output ONLY a JSON object. No markdown, no code fences." }
        $tempFile = $null

        try {
            # Write prompt to temp file to avoid command-line length limits
            $tempFile = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $tempFile -Value $currentPrompt -Encoding UTF8 -NoNewline

            $copilotPath = (Get-Command copilot -ErrorAction Stop).Source
            $psi = [System.Diagnostics.ProcessStartInfo]::new($copilotPath)
            $psi.Arguments = "-p `"$(($tempFile -replace '\\','\\' -replace '"','\"'))`" -s --allow-all-tools --model $Model --no-custom-instructions"
            # Actually: pass prompt as content read from file via subexpression won't work in args.
            # Use a wrapper: copilot reads -p from a response file isn't supported.
            # Simplest: invoke directly, capture output, with timeout.
            $responseText = & copilot -p (Get-Content $tempFile -Raw -Encoding UTF8) -s --allow-all-tools --model $Model --no-custom-instructions 2>&1 | Out-String
            Remove-Item $tempFile -ErrorAction SilentlyContinue
            $tempFile = $null
        }
        catch {
            if ($tempFile) { Remove-Item $tempFile -ErrorAction SilentlyContinue }
            $lastError = "Invocation failed: $_"
            continue
        }

        if (-not $responseText) {
            $lastError = "Copilot returned empty response"
            continue
        }

        # Extract JSON
        try {
            $parsed = Extract-JudgeJson $responseText
            $reasoning = if ($parsed.reasoning) { $parsed.reasoning } else { "No reasoning provided" }
            return [PSCustomObject]@{
                Passed    = [bool]$parsed.passed
                Reasoning = $reasoning
                Error     = $null
                Skipped   = $false
            }
        }
        catch {
            $lastError = "JSON extraction failed: $_"
            continue
        }
    }

    # All retries exhausted
    $preview = if ($responseText.Length -gt 300) { $responseText.Substring(0, 300) + "..." } else { $responseText }
    return [PSCustomObject]@{
        Passed    = $false
        Reasoning = ""
        Error     = "Failed after $maxRetries attempts. Last error: $lastError`nResponse: $preview"
        Skipped   = $false
    }
}

# ============================================================================
# Failure File
# ============================================================================
function Get-PreviousFailures {
    if (-not (Test-Path $script:FailureFile)) { return @() }
    try {
        $data = Get-Content $script:FailureFile -Raw -Encoding UTF8 | ConvertFrom-Json
        return @($data.failures)
    } catch { return @() }
}

function Save-Failures {
    param([array]$Failures)

    if ($Failures.Count -eq 0) {
        Remove-Item $script:FailureFile -ErrorAction SilentlyContinue
        return
    }
    @{ version = 1; failures = $Failures } | ConvertTo-Json -Depth 3 | Set-Content $script:FailureFile -Encoding UTF8
}

# ============================================================================
# Main
# ============================================================================

if ($RerunFailed -and $TestName) {
    Write-Error "--RerunFailed and -TestName are mutually exclusive"
    exit 1
}

if (-not (Test-Path $SpecPath)) {
    Write-Error "Spec path not found: $SpecPath"
    exit 1
}

# Check copilot CLI (unless dry-run)
if (-not $DryRun) {
    try {
        $null = & copilot --version 2>&1
    }
    catch {
        Write-Error "copilot CLI not found or not working"
        exit 1
    }
}

# Collect spec files
if (Test-Path $SpecPath -PathType Container) {
    $specFiles = @(Get-ChildItem $SpecPath -Filter "*.md" | Where-Object { $_.Name -ne "judge_prompt.md" } | Sort-Object Name)
    if ($specFiles.Count -eq 0) {
        Write-Error "No .md files found in $SpecPath"
        exit 1
    }
}
else {
    $specFiles = @(Get-Item $SpecPath)
}

# Handle --RerunFailed
$rerunFilter = @{}
if ($RerunFailed) {
    $prevFailures = Get-PreviousFailures
    if ($prevFailures.Count -eq 0) {
        Write-Host "No previous failures found. Nothing to re-run."
        exit 0
    }
    foreach ($entry in $prevFailures) {
        $key = $entry.spec_file
        if (-not $rerunFilter.ContainsKey($key)) { $rerunFilter[$key] = @() }
        $rerunFilter[$key] += $entry.test_name
    }
    $specFiles = @($specFiles | Where-Object {
        $resolved = (Resolve-Path $_.FullName).Path
        $rerunFilter.ContainsKey($resolved)
    })
    if ($specFiles.Count -eq 0) {
        Write-Host "No previous failures match current spec files. Nothing to re-run."
        exit 0
    }
}

# Run
$totalPassed = 0
$totalFailed = 0
$totalSkipped = 0
$allFailures = @()
$dryRunResults = @()

foreach ($specFile in $specFiles) {
    # Get targets
    if ($Target) {
        if (-not (Test-Path $Target)) {
            Write-Error "Target file not found: $Target"
            exit 1
        }
        $targetPaths = @($Target)
    }
    else {
        $targetPaths = @(Get-TargetsFromFrontmatter $specFile.FullName)
    }

    # Parse tests
    $content = Get-Content $specFile.FullName -Raw -Encoding UTF8
    $tests = @(Parse-SpecTests $content)

    # Filter by test name
    if ($TestName) {
        $tests = @($tests | Where-Object { $_.Name -eq $TestName })
        if ($tests.Count -eq 0) {
            Write-Host "${script:YELLOW}No test named '$TestName' found in $($specFile.Name)${script:RESET}"
            continue
        }
    }

    # Filter by rerun
    if ($rerunFilter.Count -gt 0) {
        $resolved = (Resolve-Path $specFile.FullName).Path
        $allowedNames = $rerunFilter[$resolved]
        if ($allowedNames) {
            $tests = @($tests | Where-Object { $_.Name -in $allowedNames })
        }
        if ($tests.Count -eq 0) { continue }
    }

    # Dry-run: output IR
    if ($DryRun) {
        $dryRunResults += [PSCustomObject]@{
            source = $specFile.FullName
            target = $targetPaths
            tests  = @($tests | ForEach-Object {
                [PSCustomObject]@{
                    group             = $_.Section
                    name              = $_.Name
                    intent            = $_.Intent
                    assertion         = $_.AssertionBlock
                    line_number       = $_.LineNumber
                    missing_intent    = $_.MissingIntent
                    missing_assertion = $_.MissingAssertion
                }
            })
        }
        continue
    }

    # Print header
    $targetDisplay = if ($targetPaths.Count -eq 1) { $targetPaths[0] } else { "$($targetPaths.Count) files: $($targetPaths -join ', ')" }
    Write-Host "`n${script:BOLD}Running LLM-as-Judge Tests (copilot)${script:RESET}"
    Write-Host "Spec: $($specFile.Name)"
    Write-Host "Target: $targetDisplay"
    Write-Host "Tests: $($tests.Count)"
    Write-Host ("-" * 60)

    $passed = 0; $failed = 0; $skipped = 0; $failedNames = @()

    foreach ($test in $tests) {
        Write-Host "`n$($specFile.Name):$($test.LineNumber) ${script:CYAN}$($test.Section)${script:RESET} > $($test.Name) ... " -NoNewline

        $result = Invoke-Judge -Test $test -TargetPaths $targetPaths

        if ($result.Skipped) {
            Write-Host "${script:YELLOW}SKIP${script:RESET}"
            Write-Host "  ${script:YELLOW}$($result.Reasoning)${script:RESET}"
            $skipped++
        }
        elseif ($result.Error) {
            Write-Host "${script:RED}ERROR${script:RESET}"
            Write-Host "  ${script:RED}$($result.Error)${script:RESET}"
            $failed++
            $failedNames += $test.Name
        }
        elseif ($result.Passed) {
            Write-Host "${script:GREEN}PASS${script:RESET}"
            $passed++
        }
        else {
            Write-Host "${script:RED}FAIL${script:RESET}"
            Write-Host "  $($result.Reasoning)"
            $failed++
            $failedNames += $test.Name
        }
    }

    # Per-file summary
    Write-Host ("`n" + ("=" * 60))
    $parts = @()
    if ($passed)  { $parts += "${script:GREEN}$passed passed${script:RESET}" }
    if ($failed)  { $parts += "${script:RED}$failed failed${script:RESET}" }
    if ($skipped) { $parts += "${script:YELLOW}$skipped skipped${script:RESET}" }

    if ($failed -eq 0 -and $skipped -eq 0) {
        Write-Host "${script:GREEN}${script:BOLD}All $passed tests passed${script:RESET}"
    } else {
        Write-Host "${script:BOLD}$($parts -join ', ')${script:RESET}"
    }

    $totalPassed += $passed
    $totalFailed += $failed
    $totalSkipped += $skipped
    foreach ($name in $failedNames) {
        $allFailures += @{ spec_file = (Resolve-Path $specFile.FullName).Path; test_name = $name }
    }
}

# Dry-run output
if ($DryRun) {
    if ($dryRunResults.Count -eq 1) {
        $dryRunResults[0] | ConvertTo-Json -Depth 5
    } else {
        $dryRunResults | ConvertTo-Json -Depth 5
    }
    exit 0
}

# Save failures
Save-Failures $allFailures
if ($allFailures.Count -gt 0) {
    Write-Host "`nFailures saved to $($script:FailureFile) - re-run with -RerunFailed"
}

# Multi-file summary
$totalTests = $totalPassed + $totalFailed + $totalSkipped
if ($specFiles.Count -gt 1) {
    Write-Host ("`n" + ("=" * 60))
    Write-Host "TOTAL: $($specFiles.Count) spec files, $totalTests tests"
    if ($totalFailed -eq 0 -and $totalSkipped -eq 0) {
        Write-Host "${script:GREEN}${script:BOLD}All $totalPassed tests passed${script:RESET}"
    } else {
        $parts = @()
        if ($totalPassed)  { $parts += "${script:GREEN}$totalPassed passed${script:RESET}" }
        if ($totalFailed)  { $parts += "${script:RED}$totalFailed failed${script:RESET}" }
        if ($totalSkipped) { $parts += "${script:YELLOW}$totalSkipped skipped${script:RESET}" }
        Write-Host ($parts -join ", ")
    }
}

# Exit codes: 0 = pass, 1 = failures, 2 = all skipped
if ($totalFailed -gt 0) { exit 1 }
elseif ($totalPassed -eq 0 -and $totalSkipped -gt 0) { exit 2 }
else { exit 0 }
