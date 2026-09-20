# SpecTrace — agent instructions

Extract normative requirements from a technical specification, generate test cases traceable to exact source spans, and expose coverage gaps. Every claim in the output must be verifiable against the source document by code.

This file governs work in `spectrace-dev`. The authoritative documents live in the sibling repository `spectrace-docs` and are deliberately not duplicated here.

`AGENTS.md` and `CLAUDE.md` are kept byte-identical, per implementation plan §13 — Codex reads one, Claude Code reads the other, and the rules are the same either way. Change both or neither.

---

## Before you start any task

Read, in this order:

1. `spectrace-docs/spec/tor.md` — the frozen, numbered requirements. This is *what* to build.
2. `spectrace-docs/blueprint.md` §9 — the seven non-negotiable principles. Repeated below, but read them at the source too.
3. `spectrace-docs/research/implementation-plan.md` §12 — the current milestone and its tasks.
4. The Linear card for the specific task you were given.

Then, before writing any code, state: which files you read, your plan, and how you will check the result. If required context or access is missing, say so and stop — do not start implementing around a gap.

**Precedence when documents disagree:** `blueprint.md` → `tor.md` → `implementation-plan.md`. The implementation plan is engineering reference, not authority; it may be refined as work reveals better approaches, provided every TOR requirement still holds.

---

## The seven rules

These hold regardless of model, document, or developer. They are the project's reason for existing — breaking one is not a style problem, it is the product failing.

| # | Rule |
|---|---|
| P1 | **Verification over trust.** Every claim in the output is checkable by code against the source document. |
| P2 | **The model never reports position.** It returns quote text only; our code resolves that text to a character span. |
| P3 | **Fail closed.** A requirement whose quote cannot be located is dropped and logged, never guessed into place. |
| P4 | **Provider is a parameter.** Swapping the LLM changes output quality; it must never change correctness guarantees. |
| P5 | **Reproducibility by construction.** Every model call is cached deterministically; the pipeline replays offline, with no key. |
| P6 | **Human decision is visible and separate.** The system proposes; a logged human action turns a proposal into a decision. |
| P7 | **No verdict of completeness.** Claim only "nothing in the register is uncovered", never "the specification is fully covered". |

### What they mean in code

- No prompt, schema, contract, or code path requests or accepts a model-reported character offset or line number. If you find yourself adding an `offset` field to a prompt schema — stop, that is P2.
- `SpecTrace.Core` has no compile-time or runtime dependency on `SpecTrace.Llm`. A test enforces it. Do not add one "temporarily".
- Every LLM call goes through the caching decorator. Never call a provider client directly.
- Temperature is 0 everywhere.
- No fuzzy quote matching in the default path. A `--fuzzy` flag may exist for error analysis; it defaults to off and its results never count as verified.
- A test case cannot exist without at least one requirement ID. Enforce by type, not by convention.
- Nothing in the output claims completeness. Wording matters here as much as logic.

---

## Commands

```
Build:    dotnet build --warnaserror
Test:     dotnet test
Run:      dotnet run --project src/SpecTrace.Cli -- run --document corpus/rfc6902.txt
Offline:  SPECTRACE_OFFLINE=1 dotnet run --project src/SpecTrace.Cli -- run --document corpus/rfc6902.txt
Score:    dotnet run --project src/SpecTrace.Cli -- score --run <id> --gold corpus/gold/rfc6902.gold.json
```

`Build` and `Test` work today. `Run`, `Offline` and `Score` are not implemented yet — the CLI
prints usage and exits non-zero on any argument. They land with their own tasks, and so does
`corpus/rfc6902.txt`, which is not in the repository yet.

---

## Environment

Recorded here because the implementation plan §5.2 requires the target framework choice to be
written down rather than assumed. Verified on 20 September 2026:

- `dotnet --list-sdks` reports `10.0.301` as the **only** SDK installed, which is also the
  latest LTS line. Target framework is therefore `net10.0`, set once in
  `Directory.Build.props`; no project overrides it.
- `global.json` pins `10.0.100` with `rollForward: latestFeature`, so any 10.0.x SDK on a CI
  runner satisfies it.
- `TreatWarningsAsErrors` is on for every project, not only for the build invoked with
  `--warnaserror`, because `dotnet test` builds too.
- The solution file is `SpecTrace.sln` in the classic format. This SDK's `dotnet new sln`
  defaults to `.slnx`; use `--format sln` if you ever regenerate it.
- Re-run `dotnet --list-sdks` before changing the target framework. If what you observe
  contradicts this section, the observation wins and this section is corrected.

---

## Architecture boundaries

Two halves, kept apart on purpose:

- **Verifiable core** — `SpecTrace.Core`: normalisation, span resolution, matrix assembly, metrics. No network, no file I/O, no LLM. Fully deterministic, fully unit-tested.
- **Probabilistic edge** — `SpecTrace.Llm`: the two model calls, behind one interface, cached and swappable. Everything crossing from here into Core is treated as untrusted input.

`SpecTrace.Pipeline` orchestrates the two. `SpecTrace.Cli` and `SpecTrace.Web` are thin entry points and hold no domain logic.

---

## Invariants

These run in CI against the committed cache. Do not merge with any of them red, and do not weaken one to make a test pass — if an invariant is wrong, that is a conversation, not a code change.

| # | Invariant |
|---|---|
| I1 | For every requirement, the raw text at its span normalises to exactly the requirement's text normalised. |
| I2 | Every test case's requirement IDs reference a requirement present in the register. |
| I3 | Every requirement in the register appears in the matrix exactly once. |
| I4 | Status is `gap` if and only if the requirement has zero non-rejected test cases. |
| I5 | No test case has an empty requirement ID list. |
| I6 | Requirement IDs are unique within a run, and identical across two runs over the same document with the same prompts. |
| I7 | No requirement with failed verification appears in the register or the matrix. |
| I8 | `SpecTrace.Core` has no assembly reference to `SpecTrace.Llm`. |

---

## Workflow

- One Linear issue → one branch → one PR. Branch names carry the issue ID (`spec-2-offset-map`) so Linear links them automatically.
- Work only within the task's stated boundaries. Do not expand the product, and never weaken an acceptance criterion to get a test passing.
- Hit a blocker: stop and state the fact, the cause, and the options. Do not guess past it or silently pick a direction.
- Every PR states which acceptance criteria are met and how each one was checked.
- Never report a check as done that you did not actually run.
- Replacing a real integration with a stub is acceptable only as an explicitly agreed interim step, stated in the PR.
- A new idea does not interrupt the current task — write it to `spectrace-docs` and let it be decided later. A blocking defect is the opposite: fix it or renegotiate the boundary, never hide it.
- Do not start a future milestone's tasks before the current milestone is accepted.

---

## Code conventions

- Domain types are immutable records; collections exposed as read-only.
- Prefer making an invalid state unconstructible over validating it later. `TestCase` with no requirement IDs should fail at the constructor, not in a check further down.
- Prompts live as files under `src/SpecTrace.Pipeline/Prompts/`, loaded at runtime, with the file hash included in the cache key. They are versioned artifacts, not string literals scattered through the code.
- Deterministic IDs come from content hashes, never from counters, timestamps, or GUIDs.
- No `async void`. Cancellation tokens are threaded through every I/O path.
- Tests name the behaviour being asserted, not the method under test.

---

## Data rules

- Corpus is public specifications only. Never add employer, client, or third-party material — a legal boundary, not a preference.
- No secrets in the repository. API keys come from environment variables only; `.env.example` holds names, never values.
- `cache/` is committed on purpose. It is reproducibility evidence, not clutter — do not add it to `.gitignore`.
- `runs/` is gitignored except the single reference run.

---

## Verify, don't assume

A claim about an API's behaviour or a library's capability is not verification until it has actually been run. The implementation plan marks several points **VERIFY** — treat those as instructions to check current official documentation, not as facts to copy from this repository:

- the .NET SDK version actually installed (`dotnet --list-sdks`)
- Gemini endpoint shape, request format, JSON-mode field names, and current rate limits — these have changed recently and are no longer published as one fixed number
- the section-header regex, against the real corpus file — RFC formatting varies, and a wrong regex silently mislabels every section

If something in this file or the implementation plan contradicts what you observe when you actually run it, the observation wins. Say so rather than coding around it.

---

## Out of scope

See `spectrace-docs/spec/tor.md` §2.2–2.3. Do not build anything listed there, however small it looks — scope discipline is graded on this project.

The tempting ones, repeated: no database, no authentication, no SPA or client-side build step, no PDF or OCR input, no executing the generated test cases, no retrieval or embeddings, no abstraction with a single implementation, no requirement diffing across versions, no GitHub or Linear access from the system itself.
