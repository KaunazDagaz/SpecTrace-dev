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
  source or kept out of the register;
- test case generation for each requirement the model classified testable, grounded only in that
  requirement's quote and section number, with every case a proposal until a person reviews it;
- the traceability matrix, derived from the register and the case set alone, with gaps, orphan
  cases and the human decision queue visible in `matrix.html`;
- `run`, which does all of the above and replays offline from the committed cache;
- the committed reference run in `runs/reference/`, which CI regenerates offline on Linux and
  Windows and compares byte for byte.

Not implemented yet, each with its own task in the next milestone: the review UI, scoring and
the baseline.

## Prerequisites

- .NET SDK 10.0.x. `global.json` asks for `10.0.100` or a later feature band; every project
  targets `net10.0`, set once in `Directory.Build.props`.
- Nothing else. No database, no container runtime, no API key.

## Reproduce

From the repository root, in any shell, with no API key and no network access to the model
provider:

```
dotnet run --project src/SpecTrace.Cli -- run --document corpus/rfc6902.txt --offline --out runs/reference
```

Every model call is replayed from `cache/`, and the committed reference run is rewritten in
place. Afterwards `git status` shows only `runs/reference/manifest.json` as changed, and
`git diff` shows only its `started_at` and `git_sha` lines: when the run started and which
commit built it. Every other byte is what was committed. CI runs this same command on Linux and
Windows after `tests/SpecTrace.Pipeline.Tests/OfflineEndToEndTests.cs` has compared a fresh
offline run with `runs/reference/`, file by file and byte by byte.

### Changing the reference run on purpose

The reference run only ever changes as a reviewed diff in a PR.

1. Changing anything a model request is built from (a prompt file, the model, a schema, the
   corpus, or the text of a requirement) makes the command above fail with a cache miss. The
   error names the call, the prompt file and the missing cache entry. Record the new responses
   with one online run, with `GEMINI_API_KEY` set:
   `dotnet run --project src/SpecTrace.Cli -- run --document corpus/rfc6902.txt`. It writes to
   `runs/{runId}/`, which git ignores.
2. Changing deterministic processing alone makes the offline end-to-end test fail on the first
   file that differs.
3. In both cases, run the command above, review `git diff -- runs/reference cache`, and commit
   both in the PR, saying why the output changed. Never edit `runs/reference/` by hand. Always
   write it with the offline command, so that its manifest records every call as a cache hit.

## Commands

Working today:

```
dotnet build --warnaserror     # must stay at 0 warnings, 0 errors
dotnet test                    # unit tests, invariants, and the offline end-to-end run against runs/reference

# the whole pipeline, replayed from cache/ — no key, no network
dotnet run --project src/SpecTrace.Cli -- run --document corpus/rfc6902.txt --offline

# extraction and verification only
dotnet run --project src/SpecTrace.Cli -- extract --document corpus/rfc6902.txt --offline
```

`--offline` and `SPECTRACE_OFFLINE=1` are equivalent; the flag works the same way in every
shell. `run` writes `manifest.json`, `requirements.json`, `rejected-quotes.json`,
`decisions.json`, `test-cases.json`, `matrix.json` and `matrix.html` under `runs/{runId}/`, or
under `--out`. `extract` writes `requirements.json`, `rejected-quotes.json` and
`decisions.json`. Without `--offline` or `SPECTRACE_OFFLINE=1`, either command calls Gemini for
any request not already in `cache/`, which needs `GEMINI_API_KEY` set in the environment.

`matrix.html` is a static page. Coverage in it is by proposed, unreviewed test cases, and it
does not claim the specification is fully covered: only that each requirement in the register
does or does not have a proposed case.

Documented for later, and currently not implemented — the CLI prints usage and exits with a
non-zero status if you pass it:

```
dotnet run --project src/SpecTrace.Cli -- score --run <id> --gold corpus/gold/rfc6902.gold.json
```

## Layout

```
src/SpecTrace.Core/        verifiable core: domain types, normalisation, spans, matrix, metrics
                           no network, no file I/O, no LLM dependency
src/SpecTrace.Llm/         probabilistic edge: one client interface, cached and swappable
src/SpecTrace.Pipeline/    orchestration of the two halves, prompt files
src/SpecTrace.Cli/         thin entry point: run and extract today; score, export later
tests/SpecTrace.Core.Tests/       unit tests for the core
tests/SpecTrace.Llm.Tests/        client, cache and backoff, all against fakes; one live check
tests/SpecTrace.Pipeline.Tests/   verification, invariants, offline end-to-end run against runs/reference
cache/                     committed LLM response cache
runs/reference/            committed reference run; every other run under runs/ is ignored
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
