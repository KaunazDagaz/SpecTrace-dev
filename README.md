# SpecTrace

Extract normative requirements from a technical specification, anchor each one to an exact span
of the source text, generate test cases traceable to those requirements, and expose coverage
gaps — with every claim in the output checkable by code rather than asserted by a model.

This repository holds the implementation. The concept, the agreed requirements and the
engineering plan live in a separate repository:

| Document | What it is |
|---|---|
| [SpecTrace-docs](https://github.com/KaunazDagaz/SpecTrace-docs) | The companion repository: concept, decisions, history |
| [`BLUEPRINT.md`](https://github.com/KaunazDagaz/SpecTrace-docs/blob/main/BLUEPRINT.md) | Approved concept and the seven non-negotiable principles (§9). Authoritative |
| [`spec/TOR.md`](https://github.com/KaunazDagaz/SpecTrace-docs/blob/main/spec/TOR.md) | Frozen, numbered requirements. What gets built |
| [`research/IMPLEMENTATION_PLAN.md`](https://github.com/KaunazDagaz/SpecTrace-docs/blob/main/research/IMPLEMENTATION_PLAN.md) | Engineering detail, prompts, milestones. Reference, not authority |

Requirements are never copied into this repository — one requirement must not exist in three
versions. `AGENTS.md` and `CLAUDE.md` here describe how to work in the code.

## Status

Milestone M1 (vertical slice) is in progress. What exists today:

- the corpus (`corpus/rfc6902.txt`), the whitespace normalisation with its offset map, and the
  section index;
- the LLM client behind one interface, with its disk cache, rate limiting and backoff;
- requirement extraction and fail-closed verification: every claimed quote is located in the
  source or kept out of the register, and the recorded extraction replays offline in CI.

Not implemented yet, each with its own task: test case generation, the traceability matrix,
and the full offline end-to-end `run`.

## Prerequisites

- .NET SDK 10.0.x. `global.json` asks for `10.0.100` or a later feature band; every project
  targets `net10.0`, set once in `Directory.Build.props`.
- Nothing else. No database, no container runtime, no API key.

## Commands

Working today:

```
dotnet build --warnaserror     # must stay at 0 warnings, 0 errors
dotnet test                    # unit tests, invariants, and the offline replay of the committed cache

# extraction and verification only, replayed from cache/ — no key, no network
SPECTRACE_OFFLINE=1 dotnet run --project src/SpecTrace.Cli -- extract --document corpus/rfc6902.txt
```

`extract` writes `requirements.json`, `rejected-quotes.json` and `decisions.json` under
`runs/{runId}/`. Without `SPECTRACE_OFFLINE` it calls Gemini for any request not already in
`cache/`, which needs `GEMINI_API_KEY` set in the environment.

Documented for later, and currently not implemented — the CLI prints usage and exits with a
non-zero status if you pass any of them:

```
dotnet run --project src/SpecTrace.Cli -- run --document corpus/rfc6902.txt
SPECTRACE_OFFLINE=1 dotnet run --project src/SpecTrace.Cli -- run --document corpus/rfc6902.txt
dotnet run --project src/SpecTrace.Cli -- score --run <id> --gold corpus/gold/rfc6902.gold.json
```

## Layout

```
src/SpecTrace.Core/        verifiable core: domain types, normalisation, spans, matrix, metrics
                           no network, no file I/O, no LLM dependency
src/SpecTrace.Llm/         probabilistic edge: one client interface, cached and swappable
src/SpecTrace.Pipeline/    orchestration of the two halves, prompt files
src/SpecTrace.Cli/         thin entry point: extract today; run, score, export later
tests/SpecTrace.Core.Tests/       unit tests for the core
tests/SpecTrace.Llm.Tests/        client, cache and backoff, all against fakes; one live check
tests/SpecTrace.Pipeline.Tests/   verification, invariants, offline replay of the committed cache
cache/                     committed LLM response cache
```

`SpecTrace.Web` (the review UI) belongs to the next milestone and does not exist yet.

`SpecTrace.Core` must never reference `SpecTrace.Llm`. A test in
`tests/SpecTrace.Pipeline.Tests` asserts this against both the project file and the compiled
assembly, because the compiler omits an unused reference from the assembly manifest and a
manifest check on its own would pass while the project file declared the dependency.

## Reproducibility and secrets

- `cache/` is committed on purpose. Every model response is stored under a hash of the full
  request, so a run replays offline, indefinitely, with no API key. It is evidence, not clutter.
- `runs/` is ignored except the single committed reference run.
- No secret is committed or required. `.env.example` lists variable names and no values; CI
  references no secret at all.
- API keys, when a live call is made, come from the Google Cloud project that has **no** billing
  enabled — separate from the project used for deployment, because enabling billing removes the
  Gemini free tier on that project.

## Scope

Out of scope is listed in `spec/TOR.md` §2.2–2.3 and `research/IMPLEMENTATION_PLAN.md` §11, and
none of it is built here however small it looks: no database, no authentication, no SPA or
client-side build step, no PDF or OCR input, no execution of the generated test cases, no
retrieval or embeddings, no requirement diffing across versions, and no fuzzy quote matching in
the default path.

The corpus is public specifications only.
