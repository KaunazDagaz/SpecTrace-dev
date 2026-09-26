# Headline: claimed requirements whose quote cannot be located in the document

This file is generated. The command below rebuilds it and every metrics file it names, offline, from
`cache/` and the transcripts in `experiments/a0/`, with no key and no network. CI runs the same command
and fails if the result differs from what is committed. Do not edit it by hand.

```
dotnet run --project src/SpecTrace.Cli -- score --headline --documents corpus/rfc6902.txt
```

| Document | Arm | Model | Claims | Not located | Not found | No quote | Found once | Found more than once | Calls | Tokens in / out | Finish reason | Metrics file |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| rfc6902 | A0 chat | — | not scored: pending, a0/rfc6902.md is incomplete: 'model' is still the placeholder <model name as shown in the interface>; 'share_link' is still the placeholder <public share link to the chat> | | | | | | | | | |
| rfc6902 | A baseline | gemini-3.5-flash-lite (Gemini API, temperature 0) | 16 | 81.3% (13) | 13 | 0 | 1 (6.3%) | 2 (12.5%) | 1 | 7,647 / 1,788 | STOP | [rfc6902-baseline-4227a0d51f3d.metrics.json](rfc6902-baseline-4227a0d51f3d.metrics.json) |
| rfc6902 | B pipeline, model's raw claims | gemini-3.5-flash-lite (Gemini API, temperature 0) | 18 | 0.0% (0) | 0 | 0 | 14 (77.8%) | 4 (22.2%) | 13 | 11,708 / 3,374 | not recorded | [rfc6902-3ff2234db6aa.metrics.json](rfc6902-3ff2234db6aa.metrics.json) |
| rfc6902 | B pipeline, delivered register — zero by design | gemini-3.5-flash-lite (Gemini API, temperature 0) | 12 | 0.0% (0) | 0 | 0 | 12 (100.0%) | 0 (0.0%) | — | — | — | [rfc6902-3ff2234db6aa.metrics.json](rfc6902-3ff2234db6aa.metrics.json) |

## How to read the table

- **Claims** are the items an arm presented as requirements. For A0 and A they are the items the one
  deterministic parser (`ClaimParser`) split the answer into: one claim per quote, and one claim for an
  item with no recognisable quote. For B they are the entries the extraction call returned.
- **Not located** is the headline figure: (quotes not found + claims with no quote) ÷ claims.
- **Found once** is the quote-verification rate, as the pipeline computes it: the quote occurs exactly
  once in the document. **Found more than once** means it occurs, but at several places, so no single
  span can be claimed for it. Not located, found once and found more than once add up to 100%.
- Every arm is scored by the pipeline's own matching: an exact substring match after whitespace is
  collapsed, with no fuzzy matching. The parser never reads the document and never alters a quote; it
  removes only the quotation marks or markup around it.
- **A and B are the controlled comparison**: same model, temperature 0, same document, same verifier.
  A is one naive prompt (`src/SpecTrace.Pipeline/Prompts/baseline.user.md`) with no system prompt and no
  response schema; B is the full pipeline.
- **B, delivered register: zero by design, not a finding.** The pipeline drops every quote it cannot
  locate before anything reaches the register (P3), so this share cannot be anything but zero, and the
  register also holds no quote found more than once: those go to the human decision queue. B's measured
  figure is the row for the model's raw claims.
- **A0 is illustrative and not reproducible.** It was captured by hand from a public chat interface;
  the transcript's front matter records when, which model name the interface showed, and a share
  link. The interface's model version, system prompt, sampling settings and any tools it uses are
  neither disclosed nor under our control, and a chat cannot be replayed from a cache, so asking again
  may give a different answer. Only the scoring of the archived answer is reproducible. It is reported
  for scale, not as a controlled result, and its result on each document is reported whichever way it
  came out.
- **Calls and tokens** are the provider's own counts (`promptTokenCount`, `candidatesTokenCount`),
  recorded in `cache/` when each call was made live. The chat interface reports none. Every row is
  replayed from the cache, so the cache hit rate in each metrics file is 100% by construction.
- **Finish reason** is the provider's reason for ending the whole-document answer. A was allowed the
  model's full output limit, so our own setting never cut it short; anything but `STOP` would mean the
  provider did. B's extraction is requested with a response schema, and a structured answer that stops
  for any other reason is refused and never cached; entries cached before finish reasons were kept
  show `not recorded`.
