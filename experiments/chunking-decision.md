# Chunking decision (REQ-EXT-03)

This file is generated, offline, from `cache/`, the transcripts in `experiments/a0/` and the gold standard,
by the command below. CI runs the same command and fails if the result differs from what is committed.
Do not edit it by hand.

```
dotnet run --project src/SpecTrace.Cli -- score --headline --documents corpus/rfc6902.txt,corpus/rfc10050.txt --gold corpus/gold/rfc6902.gold.yaml
```

**The rule, fixed before the measurement:** chunked extraction is needed if the pipeline's recall in the
last third of the document is at least 20 percentage points below its recall in the first third.
Recall here is B raw: every entry the extraction call returned, matched at 50% overlap, before
verification holds anything back, because the question is whether the model reads the end of the
document as well as its start. The document is split into thirds by line, and each gold requirement
belongs to the third that holds the first line of its span.

## rfc6902

| Third | Lines | Gold requirements | A0 chat (illustrative): matched, recall | A baseline: matched, recall | B raw: matched, recall | B delivered: matched, recall |
|---|---|---:|---:|---:|---:|---:|
| 1 | 1–337 | 12 | 11 of 12, 91.7% | 3 of 12, 25.0% | 11 of 12, 91.7% | 8 of 12, 66.7% |
| 2 | 338–674 | 7 | 6 of 7, 85.7% | 0 of 7, 0.0% | 7 of 7, 100.0% | 4 of 7, 57.1% |
| 3 | 675–1011 | 0 | 0 of 0, — | 0 of 0, — | 0 of 0, — | 0 of 0, — |

**Decision: the rule cannot fire on this document, so chunking is not added; single-request extraction stays, under REQ-EXT-03.**

The last third holds no gold requirement, so its recall is undefined and the comparison the rule needs cannot be made. This is an absence of evidence, not evidence that recall holds up late in a document.

- **Numbers this small are noise.** The thirds hold 12, 7, 0 gold requirements. In third 1, one requirement moves recall by 8.3 percentage points. In third 2, one requirement moves recall by 14.3 percentage points. A difference of one or two requirements between thirds says nothing about the model.
- **This check cannot see degradation late in a long document.** All of rfc6902's gold requirements sit in lines 184–422 of 1011, and the whole document is 7,980 input tokens in the pipeline's extraction call. A model that loses attention only after tens of thousands of tokens would pass it. The question stays open for longer documents, and only a gold standard on a longer document could answer it.
