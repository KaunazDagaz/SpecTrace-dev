# Headline: claimed requirements whose quote cannot be located in the document

This file is generated. The command below rebuilds it and every metrics file it names, offline, from
`cache/` and the transcripts in `experiments/a0/`, with no key and no network. CI runs the same command
and fails if the result differs from what is committed. Do not edit it by hand.

```
dotnet run --project src/SpecTrace.Cli -- score --headline --documents corpus/rfc6902.txt,corpus/rfc10050.txt --gold corpus/gold/rfc6902.gold.yaml
```

| Document | Arm | Model | Claims | Not located | Not found | No quote | Found once | Found more than once | Calls | Tokens in / out | Finish reason | Metrics file |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| rfc6902 | A0 chat — illustrative, not reproducible | Gemini 3.5 Flash-Lite (gemini.google.com) | 18 | 0.0% (0) | 0 | 0 | 14 (77.8%) | 4 (22.2%) | — | — | — | [rfc6902-chat-2026-09-26.metrics.json](rfc6902-chat-2026-09-26.metrics.json) |
| rfc6902 | A baseline | gemini-3.5-flash-lite (Gemini API, temperature 0) | 16 | 81.3% (13) | 13 | 0 | 1 (6.3%) | 2 (12.5%) | 1 | 7,647 / 1,788 | STOP | [rfc6902-baseline-4227a0d51f3d.metrics.json](rfc6902-baseline-4227a0d51f3d.metrics.json) |
| rfc6902 | B pipeline, model's raw claims | gemini-3.5-flash-lite (Gemini API, temperature 0) | 18 | 0.0% (0) | 0 | 0 | 14 (77.8%) | 4 (22.2%) | 13 | 11,708 / 3,374 | not recorded | [rfc6902-3ff2234db6aa.metrics.json](rfc6902-3ff2234db6aa.metrics.json) |
| rfc6902 | B pipeline, delivered register — zero by design | gemini-3.5-flash-lite (Gemini API, temperature 0) | 12 | 0.0% (0) | 0 | 0 | 12 (100.0%) | 0 (0.0%) | — | — | — | [rfc6902-3ff2234db6aa.metrics.json](rfc6902-3ff2234db6aa.metrics.json) |
| rfc10050 | A0 chat — illustrative, not reproducible | Gemini 3.5 Flash-Lite (gemini.google.com) | 21 | 9.5% (2) | 2 | 0 | 18 (85.7%) | 1 (4.8%) | — | — | — | [rfc10050-chat-2026-09-27.metrics.json](rfc10050-chat-2026-09-27.metrics.json) |
| rfc10050 | A baseline | gemini-3.5-flash-lite (Gemini API, temperature 0) | 22 | 9.1% (2) | 2 | 0 | 18 (81.8%) | 2 (9.1%) | 1 | 7,930 / 2,220 | STOP | [rfc10050-baseline-61a4ac164379.metrics.json](rfc10050-baseline-61a4ac164379.metrics.json) |
| rfc10050 | B pipeline, model's raw claims | gemini-3.5-flash-lite (Gemini API, temperature 0) | 30 | 3.3% (1) | 1 | 0 | 27 (90.0%) | 2 (6.7%) | 14 | 12,344 / 4,285 | STOP | [rfc10050-9759f1bdfc79.metrics.json](rfc10050-9759f1bdfc79.metrics.json) |
| rfc10050 | B pipeline, delivered register — zero by design | gemini-3.5-flash-lite (Gemini API, temperature 0) | 17 | 0.0% (0) | 0 | 0 | 17 (100.0%) | 0 (0.0%) | — | — | — | [rfc10050-9759f1bdfc79.metrics.json](rfc10050-9759f1bdfc79.metrics.json) |

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

## Extraction quality against the gold standard

rfc6902 is scored against `rfc6902.gold.yaml`: 19 requirements annotated by hand under the annotation rules frozen at SpecTrace-docs commit `69ed50e`.
rfc10050 has no gold standard and is not scored here.

| Document | Arm | Claims | Matched | Through a quote found more than once | Located, no gold match | Not located | Precision | Recall | F1 | Modality accuracy |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| rfc6902 | A0 chat — illustrative, not reproducible | 18 | 17 | 4 | 1 | 0 | 94.4% (17/18) | 89.5% (17/19) | 91.9% | n/a: the arm states no modality |
| rfc6902 | A baseline | 16 | 3 | 2 | 0 | 13 | 18.8% (3/16) | 15.8% (3/19) | 17.1% | n/a: the arm states no modality |
| rfc6902 | B raw | 18 | 18 | 4 | 0 | 0 | 100.0% (18/18) | 94.7% (18/19) | 97.3% | 77.8% (14/18) |
| rfc6902 | B delivered | 12 | 12 | 0 | 0 | 0 | 100.0% (12/12) | 63.2% (12/19) | 77.4% | 83.3% (10/12) |

### The same matching at 30% and 70% overlap

| Document | Arm | 30%: P / R / F1 | 50%: P / R / F1 | 70%: P / R / F1 | Smallest overlap of a matched pair at 50% |
|---|---|---|---|---|---:|
| rfc6902 | A0 chat — illustrative, not reproducible | 94.4% / 89.5% / 91.9% | 94.4% / 89.5% / 91.9% | 94.4% / 89.5% / 91.9% | 100.0% |
| rfc6902 | A baseline | 18.8% / 15.8% / 17.1% | 18.8% / 15.8% / 17.1% | 18.8% / 15.8% / 17.1% | 100.0% |
| rfc6902 | B raw | 100.0% / 94.7% / 97.3% | 100.0% / 94.7% / 97.3% | 100.0% / 94.7% / 97.3% | 100.0% |
| rfc6902 | B delivered | 100.0% / 63.2% / 77.4% | 100.0% / 63.2% / 77.4% | 100.0% / 63.2% / 77.4% | 100.0% |

### Cost of verification

| Document | Arm | Quotes not located | Of those, similarity ≥ 0.90 | Gold reached but lost | Gold held back from the register |
|---|---|---:|---:|---:|---|
| rfc6902 | A0 chat — illustrative, not reproducible | 0 | 0 | 0 | — |
| rfc6902 | A baseline | 13 | 12 | 12 | — |
| rfc6902 | B raw | 0 | 0 | 0 | — |
| rfc6902 | B delivered | 0 | 0 | 0 | 6: 9.1, 11.1, 13.1, 16.1, 19.1, 19.2 |

### How to read these tables

- **A claim counts only through a quote that locates in the source.** A claim whose quote cannot be
  found verbatim, after whitespace is collapsed, is a false positive however close its text comes to a
  requirement. Nothing the approximate matching under *Cost of verification* finds is ever counted as
  matched.
- **Matched**: a located claim's span overlaps a gold requirement's span by at least 50% of the shorter
  of the two. Each gold requirement matches at most one claim and each claim at most one gold
  requirement, greedily by overlap size, largest first; ties go to the gold requirement earlier in the
  document, then to the earlier claim. A claim whose quote is found more than once may match through
  any of its occurrences; those matches are also counted in their own column.
- **Precision** = matched ÷ claims, every claim counted, located or not. **Recall** = matched ÷ gold
  requirements. **F1** is their harmonic mean.
- **Modality accuracy** = matched pairs whose modality equals the gold modality ÷ matched pairs. A0 and A
  show n/a: the naive prompt they share asks for no modality and their answers state none, so a
  modality read off the keyword inside the quote would be our reading, not the arm's.
- **B raw** scores every entry the extraction call returned. **B delivered** scores the register, the
  requirements the pipeline delivers after verification; a quote found more than once, or claimed twice
  with different modalities, goes to the human decision queue instead, and the gold requirements it
  matched in B raw are the ones *held back from the register*.
- **30% and 70%**: 50% is a judgment call, so the same matching is repeated with the other two
  thresholds; nothing else changes. A threshold can only change a pair whose overlap lies between the
  two thresholds compared, so the last column bounds how far the 50% figures could move.
- **Cost of verification**: for every quote an arm gave that cannot be located, the closest stretch of
  the document by Levenshtein distance; similarity = 1 − distance ÷ quote length, threshold 0.90,
  both fixed before the analysis was run. *Gold reached but lost* counts the gold requirements such a
  quote lands on, by the same 50% overlap, that no located claim of the same arm matched: the model
  reached them, and lost them because its quote was not verbatim.
- Every claim, match and missed requirement on rfc6902: [rfc6902.quality.md](rfc6902.quality.md).
- Recall by third of the document and the chunking decision: [chunking-decision.md](chunking-decision.md).
- The error analysis, written by hand from these files and not generated: [error-analysis.md](error-analysis.md).
