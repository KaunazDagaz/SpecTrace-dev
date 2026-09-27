# Corpus sources

Every file in `corpus/` is a public IETF specification, stored exactly as the RFC Editor
publishes it: in full, without modification, byte for byte. That is the condition under which
the IETF Trust licenses copying and redistribution (Trust Legal Provisions 5.0, corrected,
section 3.c.i: "to copy, publish, display and distribute IETF Contributions and IETF Documents
in full and without modification"). Do not re-save, re-encode, reflow or trim these files.
`.gitattributes` marks `corpus/**` as `-text` so git never changes their line endings.

Each RFC below states in its own Copyright Notice that it "is subject to BCP 78 and the IETF
Trust's Legal Provisions Relating to IETF Documents (https://trustee.ietf.org/license-info) in
effect on the date of publication of this document". The provisions in effect today are at
<https://trustee.ietf.org/license-info>.

| File | Document | Published | Stream and status | Source | Bytes | SHA-256 | Retrieved |
|---|---|---|---|---|---|---|---|
| `rfc6902.txt` | RFC 6902, JavaScript Object Notation (JSON) Patch | April 2013 | IETF, Proposed Standard | <https://www.rfc-editor.org/rfc/rfc6902.txt> | 26,405 | `ded8fa1754ab1566fd72611c9d5bcde931cd2bdf8351889e2ef5b50efdf1eed4` | added 20 September 2026; identical to the RFC Editor's copy on 27 September 2026 |
| `rfc10050.txt` | RFC 10050, Protocol-Specific Profiles for JSContact | September 2026 | IETF, Proposed Standard | <https://www.rfc-editor.org/rfc/rfc10050.txt> | 31,890 | `1078762128df637d5df7e3b64eef26d3c41760840e8774c7ac3957e4214f0ba9` | 27 September 2026 |

## Why each document is here

**RFC 6902** is the primary document: precise, numbered, normatively dense, and small enough to
fit whole in one request. It is also well known and likely present in any model's training data.

**RFC 10050** is the second, less familiar document for the contamination check (TOR REQ-EXP-05).
It was chosen from three candidates: RFC 10050, RFC 10031 and RFC 10022. All three are recent
Standards Track RFCs of similar size, and none revises or updates another RFC.

- **Publication and training data.** RFC 10050 was published in September 2026. The pipeline's
  model, `gemini-3.5-flash-lite`, has a knowledge cutoff of March 2026 according to its model
  card. The Internet-Draft it grew from was public before that date: the first draft appeared in
  February 2025, and 17 draft versions were published before April 2026. So the RFC postdates the
  model's training data, but most of its text did not.
- **Encoding.** The file is UTF-8 and begins with a byte order mark, as every current text-format
  RFC does; RFC 6902 has none. .NET drops the mark when it reads the file, so offsets and quote
  matching start at the first real character. It contains two non-ASCII characters in an
  example in Appendix A.2.
- **Section index.** Checked against the file:
  `tests/SpecTrace.Core.Tests/Rfc10050SectionIndexTests.cs` asserts that the index holds exactly
  the 17 sections of the document's table of contents, in order, and that 13 sampled spans
  report the section a reader finds them in.
