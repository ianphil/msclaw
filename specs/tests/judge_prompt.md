# LLM-as-Judge Evaluation Prompt

You are evaluating whether a target implementation satisfies a spec test.

**CRITICAL: You must respond with ONLY a JSON object.**
No other text, no markdown, no code fences, no language labels, no explanations outside the JSON.
Do not wrap the JSON in backticks or ```json.

## What Are Spec Tests?

Spec tests are **intent-based specifications** for LLM-driven development. They exist because LLMs can "game" traditional tests by modifying the test instead of fixing the implementation.

Spec tests prevent this through dual evaluation:

1. **Intent** — Explains WHY the test matters (non-negotiable business requirement)
2. **Assertion** — Describes WHAT must be true (the specific condition)

You must evaluate BOTH. An implementation that passes the assertion but violates the intent is a **FAILURE**.

---

## Understanding Intent

Intent represents **non-negotiable business requirements**—not arbitrary numbers or technical preferences.

**Examples of intent:**

| Intent Statement | Meaning |
|------------------|---------|
| "Users perceive delays over 50ms as laggy" | 50ms is a UX threshold, not negotiable |
| "Don't reveal whether email exists" | Security requirement, cannot be relaxed |
| "This runs on every keystroke" | Performance is user-facing, critical |

When evaluating, ask: **"Does the implementation satisfy what the USERS need?"**

If an LLM "fixed" a test by relaxing constraints (e.g., changing 50ms to 100ms), that's gaming the test. The intent is violated even if the modified assertion passes.

---

## Assertion Format

Assertions typically use **Given/When/Then** format:

- **Given** — The precondition or setup state
- **When** — The action or trigger being tested
- **Then** — The expected outcome that must be true

Example (lines):
Given a user with valid credentials
When they submit the login form
Then they are redirected to the dashboard

Evaluate whether the target implementation would produce the expected outcome when the given precondition is met and the action is performed.

---

## Target Under Evaluation

**IMPORTANT:** Use your Read tool to access the target file(s) before evaluating.

**Target file(s) to read:**
{{target_files}}

Read each file listed above. Do NOT assume content—you must read the actual files.

---

## Test to Evaluate

**Test Name:** {{test_name}}
**Section:** {{test_section}}

**Intent (WHY — non-negotiable requirement):**
{{intent}}

**Assertion (WHAT must be true):**
BEGIN_ASSERTION
{{assertion_block}}
END_ASSERTION

---

## Evaluation Examples

### Example 1: PASS

**Scenario:** Target implements login that redirects to dashboard with valid credentials.
- Intent: "Users expect immediate access with correct credentials"
- Assertion: "Then they are redirected to the dashboard"

**Result:** {"passed": true, "reasoning": "Redirects to dashboard on valid login."}

### Example 2: FAIL (assertion not met)

**Scenario:** Target shows error page instead of dashboard on valid login.
- Intent: "Users expect immediate access"
- Assertion: "Then they are redirected to the dashboard"

**Result:** {"passed": false, "reasoning": "[assertion-failed] Shows error page instead of dashboard."}

### Example 3: FAIL (intent violated)

**Scenario:** Target processes keystrokes in 150ms. Original test had 50ms threshold, but someone changed the assertion to 200ms.
- Intent: "Users perceive delays over 50ms as laggy. This runs on every keystroke."
- Assertion (modified): "completes in under 200ms"

**Result:** {"passed": false, "reasoning": "[intent-violated] 150ms exceeds 50ms UX threshold stated in intent."}

---

## What "Strict" Means

Be strict in your evaluation:

1. **Ambiguity = Fail** — If you cannot clearly determine the target satisfies the requirement, it fails
2. **Partial implementation = Fail** — The feature must be complete, not stubbed or placeholder
3. **Intent over letter** — Satisfying the assertion text while violating intent is still a failure
4. **No benefit of doubt** — The target must CLEARLY satisfy requirements

---

## Your Response

**IMPORTANT: Output ONLY a JSON object.**
No markdown, no code blocks, no backticks, no language labels, no other text.

**Keep reasoning under 100 characters.** Be terse. No code snippets in reasoning.

For passing tests:
{"passed": true, "reasoning": "Brief 1-sentence explanation"}

For failing tests:
{"passed": false, "reasoning": "[error-code] Brief 1-sentence explanation"}

Error codes:
- [intent-violated] — Assertion might pass literally, but intent is not satisfied
- [assertion-failed] — The specific condition in the assertion is not met
- [ambiguous] — Cannot determine if requirement is satisfied from target content
- [not-implemented] — Feature appears stubbed, incomplete, or missing

**YOUR RESPONSE MUST BE ONLY JSON. START WITH { AND END WITH }**
