# PoE Market Price Overlay — working rules

## Design documents come first

**Any feature addition or change must land in the design documents before it lands in code.** Not afterwards, not "I'll write it up once it works". If you are about to add a capability, change a behaviour, or alter a signature, amend the design first and let the code follow.

The chain, from authority downward:

| Document | Role |
|---|---|
| `docs/REQUIREMENTS.md` | What the app must do. 42 requirement IDs. The authority for scope. |
| `docs/design/00-api-contract.md` | The measured poe.ninja contract. **Binding.** |
| `docs/design/00-shell-measurements.md` | Measured Win32/rendering facts. **Binding — outranks every design claim.** |
| `docs/design/01-hld.md` | Architecture and settled decisions D1–D22. |
| `docs/design/02-lld-core.md` | Core's eight modules. |
| `docs/design/03-lld-shell.md` | Shell and Presentation. |
| `docs/design/04-dld.md` | Signatures, JSON names, error codes, test layout, constants. |

Rules that follow from that ordering:

- **A measurement outranks a design claim.** If code contradicts a measured fact, the code is wrong — unless you re-measure and the fact does not hold, in which case correct the measurement document first and say what changed.
- **When a lower document contradicts a higher one, fix the documents before writing code.** A contradiction is a real defect; working around it silently is how it survives to the next stage.
- **If the design turns out to be wrong or unimplementable, report it — do not work around it.** On this project that instruction produced the most valuable output at every single stage.
- **Amend by editing the frozen text, not by adding a note beside it.** A half-applied amendment that leaves the old sentence standing is worse than none: it reads as a contradiction, and an implementer may follow either half.

## Measuring

Empirical probes have overturned design claims here seventeen times, and **five of those were our own earlier measurements**. Before trusting one, ask whether the experiment actually discriminates:

- **Does it separate the competing hypotheses?** A timer measurement was consistent with two different models and settled neither.
- **Could the right and wrong answers coincide?** One computation's correct output matched a field the code could have echoed instead; no test could tell them apart.
- **Does it observe the transition, or only the end state?** A merge-order test ended in the same state under either rule.
- **Is the thing measured the thing specified?** A convergence bound was measured with one queued command while the design queued two.
- **Did the configuration under test actually come into being?** A window was measured as layered when the style bit had been silently dropped — the result was true of a configuration that never existed.

Record measurements in `00-shell-measurements.md` **before** designing on them, with the numbers and the probe that produced them.

## Testing

Build settings promote warnings to errors, including CA2007 and CA1031. `Presentation/` carries the one CA2007 exemption.

- **A new regression test is not trusted until you have reverted the guard and watched it fail.** Report that evidence.
- Assert on observable state, never on a mock having been called.
- Passing tests have hidden real defects here three ways: asserting the buggy shape itself, counting events without checking which event, and using a fake clock that never advances so two separate reads returned the same value.
- CA2007 does **not** flag `await foreach`. The convention still applies there; no analyzer will tell you.

## Conventions

- `ConfigureAwait(false)` on every `await` outside `Presentation/`.
- No `null!`, no `default!`. If you need one, the type is wrong.
- Failure is a value, not an exception — except for programming errors and cancellation, which is control flow.
- Time arrives through `TimeProvider`; nothing calls `DateTimeOffset.UtcNow` directly. `Pricing` has no clock at all — time is a parameter, and there is deliberately nowhere to put one.
- Every `catch` must appear on the allow-list in `02-lld-core.md` §9.5 and must produce an observable result.
