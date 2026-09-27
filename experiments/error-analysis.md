# Error analysis: extraction on RFC 6902 against the gold standard

**Drafted by the agent** (Claude Opus 5.5, 27 September 2026, SPEC-12). Every paragraph headed *Agent's reading*
is a draft conclusion for the student to accept, change or reject. The verdict column in section 5 belongs to
the student and is left empty.

This file is written by hand; it is not generated. Every figure in it comes from files the headline command
regenerates offline and CI checks byte for byte: [headline.md](headline.md),
[rfc6902.quality.md](rfc6902.quality.md), [chunking-decision.md](chunking-decision.md) and the metrics files. If
a figure here disagrees with those files, those files are right. A test checks that section 5 lists exactly
the unmatched claims [rfc6902.quality.md](rfc6902.quality.md) lists.

**Definition used throughout.** A claim counts only through a quote that locates in the source: its quote must
be found verbatim in the document after whitespace is collapsed. A claim that cannot be located is a false
positive however close its text comes to a requirement. A located claim matches a gold requirement when their
spans overlap by at least 50% of the shorter span, one to one, greedy by overlap size.

## 1. Where the analysis starts

RFC 6902, 19 gold requirements under the frozen annotation rules (`69ed50e`).

| Arm | Claims | Matched | Not located | Precision | Recall | F1 | Modality |
|---|---:|---:|---:|---:|---:|---:|---|
| A0 chat — illustrative, not reproducible | 18 | 17 | 0 | 94.4% | 89.5% | 91.9% | n/a |
| A baseline | 16 | 3 | 13 | 18.8% | 15.8% | 17.1% | n/a |
| B pipeline, raw claims | 18 | 18 | 0 | 100.0% | 94.7% | 97.3% | 14/18 |
| B pipeline, delivered register | 12 | 12 | 0 | 100.0% | 63.2% | 77.4% | 10/12 |

The quality figures are for RFC 6902 only. RFC 10050 has no gold standard.

## 2. Quotes that cannot be located

Every quote any arm gave that the verifier could not find, on both documents. A quote that cannot be located
needs no gold standard to be classified, so RFC 10050 is included here.

| Document | Arm | Not located | Quotation marks changed | Elision marker added | Letter case changed |
|---|---|---:|---:|---:|---:|
| rfc6902 | A0 chat | 0 | — | — | — |
| rfc6902 | A baseline | 13 | 12 | 1 | — |
| rfc6902 | B pipeline | 0 | — | — | — |
| rfc10050 | A0 chat | 2 | — | 2 | — |
| rfc10050 | A baseline | 2 | — | 2 | — |
| rfc10050 | B pipeline | 1 | — | — | 1 |

**Quotation marks changed.** Every double quotation mark inside the sentence became a single one:
`exactly one 'op' member` where RFC 6902 has `exactly one "op" member`. This covers A baseline claims 1–4, 8
and 10–16 on RFC 6902. The words are all there, in order.

**Elision marker added.** The model marked where it cut the sentence. On RFC 6902, A baseline claim 5 is
`When the operation is applied, the target location MUST reference one of: [...]`. The source ends the line at
`one of:` and continues with a list. On RFC 10050, four quotes begin with `...` before a clause that starts in
the middle of the source sentence:

- A baseline claim 2: `...a profile MUST NOT loosen restrictions.`
- A baseline claim 11: `...such multiple entries MUST NOT result in conflicting restrictions for the same property.`
- A0 chat claim 9: the same quote as A baseline claim 11.
- A0 chat claim 13: `...in which case all instances of the object MUST have this property set to one of the allowed values.`

**Letter case changed.** The pipeline's one rejected quote on RFC 10050 is
`The protocol specification MAY define additional restrictions that a profile cannot express.` The source has
`the protocol specification MAY ...`, the clause after a semicolon. The model capitalised the first letter.

**Not seen anywhere:** a paraphrase, two sentences merged into one quote, or a word replaced by another.

*Agent's reading.* No unlocated quote in this experiment is invented or reworded. Each one is the source
tidied: its typography, its capitalisation, or a mark showing where the model cut it. The implementation plan
§8.6 asks whether failed quotes come from paraphrasing, merging sentences or correcting the source. On this
evidence it is the third, in its most cosmetic form. The strict verifier treats a tidied quote exactly as it
treats an invented one. That is by design (P3), and the price of that design is section 3.

## 3. The cost of verification

**The question as asked, for the pipeline: 0.** On RFC 6902 the pipeline rejected no quote as not found. Its
`rejected-quotes.json` is empty. No gold requirement was reached and then lost because a quote was not
verbatim, so there is no matched pair to judge. The one quote it rejected on RFC 10050 is the letter-case
change above. It cannot be scored, because RFC 10050 has no gold standard.

The similarity measure and its threshold were fixed before the analysis was run. The measure is 1 − (Levenshtein
distance between the quote and the closest stretch of the document) ÷ quote length, both whitespace-collapsed.
The closest gold requirement is the one that stretch overlaps by at least 50% of the shorter span. The
threshold is 0.90. Comparing a quote with the gold quote text directly was ruled out, because three gold
entries (4.1, 4.3, 4.6) share the identical quote `The operation object MUST contain a "value" member`, and text
alone cannot say which of them is closest.

**Where the pipeline's verification does cost recall: quotes it holds back.** B raw matched 18 gold
requirements. The register matches 12. The six in between were reached by the model and kept out of the
register by the verifier's other rules:

| Gold | Section | Claim in B raw | Why it is not in the register |
|---|---|---|---|
| 9.1 | 4.2 | 8 | the quote occurs in §4.2 and §4.3, so no single span can be claimed |
| 11.1 | 4.3 | 9 | the same quote as claim 8 |
| 13.1 | 4.4 | 11 | the quote occurs in §4.4 and §4.5 |
| 16.1 | 4.5 | 14 | the same quote as claim 11 |
| 19.1 | 5 | 18 | the §5 sentence was claimed twice, as SHOULD and as MUST_NOT |
| 19.2 | 5 | 17 | the same sentence as claim 18 |

That is 31.6 percentage points of recall: 94.7% raw against 63.2% delivered. These requirements are held
back, not lost. Each one is an item in the human decision queue of the reference run (`decisions.json`), with
a question a reviewer can answer.

**Supplementary: the same measure on the baseline.** This was not asked for. It is reported because the
pipeline gave the measure nothing to work on. Disclosure: the baseline's claims were read while surveying the
repository, before the measure and threshold were fixed, so this part is not blind.

Of the baseline's 13 unlocated quotes, 12 are at or above 0.90. They land on 12 gold requirements that no
located baseline claim matched. The baseline reached 3 + 12 = 15 of 19 gold requirements. Verification counts
3.

Every pair, for the student to judge (similarity, then the baseline quote's alteration):

| Claim | Similarity | Edits | Gold | Alteration |
|---|---:|---:|---|---|
| 1 | 0.980 | 2 | 2.1 | `"op"` → `'op'` |
| 3 | 0.971 | 2 | 4.1 | `"path"` → `'path'` |
| 4 | 0.979 | 2 | 6.1 | `"value"` → `'value'` |
| 5 | 0.937 | 5 | 7.1 | ` [...]` appended |
| 8 | 0.979 | 2 | 10.1 | `"value"` → `'value'` |
| 10 | 0.989 | 2 | 12.1 | `"from"` → `'from'` |
| 11 | 0.970 | 2 | 13.1 | `"from"` → `'from'`; the sentence occurs in §4.4 and §4.5, and claim 11 took 13.1 |
| 12 | 0.969 | 4 | 14.1 | `"from"`, `"path"` → `'from'`, `'path'` |
| 13 | 0.989 | 2 | 15.1 | `"from"` → `'from'` |
| 14 | 0.970 | 2 | 16.1 | as claim 11; claim 14 took 16.1 |
| 15 | 0.983 | 2 | 17.1 | `"value"` → `'value'` |
| 16 | 0.980 | 2 | 18.1 | `"value"` → `'value'` |

Below the threshold, and so not counted:

| Claim | Similarity | Edits | Closest gold | Alteration |
|---|---:|---:|---|---|
| 2 | 0.885 | 12 | 3.1 | six quoted words, `"add"` … `"test"` → `'add'` … `'test'` |

*Agent's reading.* Claim 2 is the same alteration as the twelve counted pairs. It falls below 0.90 only because
its sentence quotes six words, so the same mechanical change costs twelve edits. The threshold separates it
from the others by the density of quotation marks, not by how far the model strayed.

*Agent's reading.* The exchange rate asked in the implementation plan §8.1, on this document:
- Pipeline: zero unverifiable claims delivered, bought with 6 of 19 gold requirements moved from the register to
  the decision queue. None was lost to a non-verbatim quote.
- Baseline, with the same model: it reached 15 gold requirements and verification credits 3. Nearly all of its
  81.3% not-located share is one formatting habit, applied consistently.

## 4. Gold requirements the arms missed

All 19 gold requirements sit in RFC 6902 §4 to §5, lines 184–422 of 1011. So does every miss.

| Arm | Missed | Which, and why |
|---|---:|---|
| B raw | 1 | 10.1 (§4.3, line 303, `The operation object MUST contain a "value" member`, for "replace"). The model never quoted this sentence. Claims 8 and 9 both quote `The target location MUST exist for the operation to be successful.`, which §4.2 and §4.3 share. The model covered that sentence twice and skipped the sentence before it in §4.3. |
| B delivered | 7 | 10.1, and the six held back in section 3. |
| A0 chat | 2 | 7.1 (§4.1, `When the operation is applied, the target location MUST reference one of:`). The chat quoted a nearby §4.1 sentence without a keyword instead (claim 6, section 5). 19.1 (§5, SHOULD). The chat's one §5 claim quotes the whole sentence and matched 19.2, the longer clause. One claim matches at most one gold requirement, so the sentence's second obligation was left without a claim. |
| A baseline | 16 | 12 reached but lost to non-verbatim quotes (section 3). 3.1, below the similarity threshold. 5.1 (§4, `Members that are not explicitly defined ... MUST be ignored`), 19.1 and 19.2 (§5): the baseline never quoted these sentences. |

*Agent's reading.* The misses cluster where the document repeats itself or packs two obligations into one
sentence:
- the shared sentence in §4.2 and §4.3;
- the shared sentence in §4.4 and §4.5;
- the minimal clause shared by §4.1, §4.3 and §4.6;
- the two obligations in the §5 sentence.

They come from how the document is written, not from where in it they sit.

## 5. Unmatched claims: verdicts for the student

Every claim that matched no gold requirement at 50%. For each, choose **annotation miss** (the gold standard
lacks a requirement it should hold under the frozen rules) or **model false positive**. The agent leaves this
column empty.

Two facts for the verdicts, neither of them a verdict:
- A0 chat claim 6 is the sentence the annotation rules §1 list as an example of what is not a requirement:
  "normative in effect, but it carries no keyword".
- The 13 A baseline rows are not located. The counting rule makes each a false positive whatever the verdict.
  Section 3 places 12 of them on gold requirements.

| Arm | Claim | Section | Quote | Verdict (annotation miss / model false positive) |
|---|---|---|---|---|
| A0 chat (illustrative) | 6 | 4.1 | However, the object itself or an array containing it does need to exist, and it remains an error for that not to be the case. | |
| A baseline | 1 | not located | Operation objects MUST have exactly one 'op' member, whose value indicates the operation to perform. | |
| A baseline | 2 | not located | Its value MUST be one of 'add', 'remove', 'replace', 'move', 'copy', or 'test'; other values are errors. | |
| A baseline | 3 | not located | Additionally, operation objects MUST have exactly one 'path' member. | |
| A baseline | 4 | not located | The operation object MUST contain a 'value' member whose content specifies the value to be added. | |
| A baseline | 5 | not located | When the operation is applied, the target location MUST reference one of: [...] | |
| A baseline | 8 | not located | The operation object MUST contain a 'value' member whose content specifies the replacement value. | |
| A baseline | 10 | not located | The operation object MUST contain a 'from' member, which is a string containing a JSON Pointer value that references the location in the target document to move the value from. | |
| A baseline | 11 | not located | The 'from' location MUST exist for the operation to be successful. | |
| A baseline | 12 | not located | The 'from' location MUST NOT be a proper prefix of the 'path' location; i.e., a location cannot be moved into one of its children. | |
| A baseline | 13 | not located | The operation object MUST contain a 'from' member, which is a string containing a JSON Pointer value that references the location in the target document to copy the value from. | |
| A baseline | 14 | not located | The 'from' location MUST exist for the operation to be successful. | |
| A baseline | 15 | not located | The operation object MUST contain a 'value' member that conveys the value to be compared to the target location's value. | |
| A baseline | 16 | not located | The target location MUST be equal to the 'value' value for the operation to be considered successful. | |

The pipeline has no unmatched claim, raw or delivered.

## 6. Modality

- **B delivered, 10 of 12.** Both errors are the same one: a `MUST NOT` sentence classed as `MUST`. They are 8.1
  (§4.1, `The specified index MUST NOT be greater than ...`) and 14.1 (§4.4,
  `The "from" location MUST NOT be a proper prefix ...`). RFC 6902 has only these two `MUST NOT` sentences, so
  the model missed the negation in both.
- **B raw, 14 of 18.** It has the same two errors, plus the §5 pair.
  - Claims 17 (SHOULD) and 18 (MUST_NOT) quote the same whole sentence, and each covers both gold clauses
    fully.
  - Greedy by overlap size gives the longer clause, 19.2 (MUST_NOT), to claim 17 and then 19.1 (SHOULD) to
    claim 18. Both pairs disagree on modality. Under the other pairing both would agree.
  - Span matching cannot tell which reading goes with which clause, and the metric counts two errors. Without
    this pairing the figure would be 16 of 18. That is context only; the metric stays as computed.
  - A test (`TwoReadingsOfOneSentenceArePairedWithItsTwoObligationsBySpanAloneSoTheirModalitiesMayCross`) pins
    this behaviour down.
- **A0 and A: n/a.** The naive prompt they share asks for no modality, and their answers state none. A modality
  read off the keyword inside the quote would be our reading, not the arm's.

## 7. The 30% and 70% thresholds

No figure moves for any arm. Every matched pair covers 100% of the shorter span: each located claim quotes
the whole sentence or clause that contains its gold clause. No claim sits partly over a requirement. On RFC
6902 the choice of 50% does not matter. That would change on a document where models quote fragments that
straddle sentence boundaries.

## 8. The chat arm on both documents (REQ-EXP-05)

The chat arm is illustrative and not reproducible. It was captured by hand from gemini.google.com. See the
transcripts' front matter and [headline.md](headline.md).

- **RFC 6902:** 0.0% not located (0 of 18 claims). Precision 94.4%, recall 89.5%, F1 91.9%. The chat arm does
  well on RFC 6902. It beats the baseline on every figure and comes within one or two requirements of the
  pipeline's raw claims.
- **RFC 10050:** 9.5% not located (2 of 21 claims), both from the `...` elision marker. There is no gold
  standard, so there is no precision or recall.

*Agent's reading.*
- RFC 6902 is a well-known 2013 RFC. The chat result there is consistent with the model having seen it
  before, as TOR §11 assumes, but it does not show that.
- The chat's not-located share rises from 0% to 9.5% on the less familiar document. Two claims are too few to
  read anything into.
- The baseline moves the other way: 81.3% not located on RFC 6902 against 9.1% on RFC 10050. That points to
  RFC 6902's many quoted words, which the baseline rewrote, rather than to familiarity with the text.

## 9. Test cases rejected on review

The implementation plan §8.6 also asks which test cases reviewers reject. No review has been recorded yet:
review lands with SPEC-13. There is nothing to analyse here until then.

## 10. Conclusions

*Agent's reading, all of it.*

1. On RFC 6902 the pipeline's strict verification cost no recall through non-verbatim quotes. Its cost came
   from the rule that a quote found more than once, or read two ways, goes to a person. That rule holds back 6
   of 19 gold requirements (31.6 points of recall), none of them lost.
2. For the naive baseline, verification is the whole story. It separates 15 gold requirements reached from 3
   credited, and the cause is one consistent formatting habit, not invention.
3. Chunking is not added. The rule cannot fire, because RFC 6902 has no gold requirement in its last third,
   and the check says nothing about long documents ([chunking-decision.md](chunking-decision.md)).
4. Findings that could each become a new task. None is started here, and each needs a decision:
   - the pipeline's extraction reads `MUST NOT` as `MUST` in both of the document's `MUST NOT` sentences;
   - the pipeline quoted the §4.2/§4.3 shared sentence twice and skipped §4.3's first sentence;
   - four of the six held-back gold requirements are a quote the model claimed once for each place it occurs.
     A human decision naming the occurrence could anchor each of them. SPEC-13 logs such answers but, in M2,
     changes no register with them.
