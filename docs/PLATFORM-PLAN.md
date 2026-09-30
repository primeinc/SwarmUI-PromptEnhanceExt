# PromptEnhance Platform Plan

## Charter

PromptEnhance is the first-party reference implementation for SwarmUI extensions — and beyond an exemplar, a platform extension. It establishes the golden path for extension authors (canonical project layout, build/test gates, contract-driven config, error taxonomy, CI), and applies SwarmUI's own architectural pattern one layer down: a microkernel design in which the foundation exposes stable, versioned extension points — a service-provider interface (SPI) for LLM transport, settings persistence, and prompt-mutation policy — so downstream extensions compose against a supported public API instead of each hand-rolling the same infrastructure. The design is validated by downstream adoption: a strangler-fig migration of MagicPrompt that deletes its duplicated infrastructure, re-platforms it as a thin feature extension consuming the foundation's SPI, and demonstrates that the maintainer's intended feature set is expressible as plugins on the platform.

Sequencing principle: integration risk (host-level extension→extension loading) is deferred to the last responsible moment. SPI sufficiency is validated by compile-time composition first; SwarmUI core changes come only after a validated product exists.

## Standing rules (apply to every stage)

- **Ground truth**: every claim about SwarmUI behavior cites `path:line` at the pinned ref (`swarmui_pin` in `justfile` == ref in `.github/workflows/gates.yml`). Every claim about the OpenAI wire protocol cites `openapi.yaml:line` in `../refs/openai/openai-openapi`. Absence claims require a same-tool positive control (`VALIDATED_EMPTY` or it is unknown).
- **Mechanical exit gates** (all stages): `npm run check:frontend-parity`, `npm run test:frontend`, `dotnet test Tests/PromptEnhance.Tests.csproj -c Debug`, `just vendor-ci-test`. A stage is not done with any gate red.
- **Hostile review**: each stage ends with an adversarial-oracle pass scoped to the stage's diff and claims. Findings are dispositioned CONFIRMED→fixed or REFUTED→evidence recorded in the stage's commit message. No unreviewed stage merges.
- **Falsification probes**: each stage defines at least one probe that MUST fail when the work is sabotaged. A gate that cannot be made to fail is not a gate. Probes are run once, shown failing, then reverted.
- **SPI design rule**: no public seam may depend on compile-time-only facts (static initialization order, shared statics across extensions, internal types) — the Stage 6 loader must not be able to force SPI changes.
- **Comment/doc discipline**: code comments state constraints only; teaching content lives in `docs/`; vendor-mandated rationale (README connections table) is exempt.
- **Agent-code gate**: nothing agent-authored runs ungated — including orchestrator-built instruments (monitors, probes, scripts). Every instrument proves it can see its target at arm time (positive control) and is shown alarming on a broken target (falsification) before its silence is trusted; silent-failure fallbacks that conflate absent/failed/empty are banned.
- **Vendor/SDK-first**: maintained SDKs and vendor libraries over hand-rolled equivalents, always. Mandatory input to every ADR and every dispatched brief; a conclusion against it is an owner escalation, never an agent ratification. Hand-rolled code is legitimate only where no maintained client exists (the owned SPI adapter layer).

## Stage 0 — Ground truth (read-only, time-boxed)

Goal: the three facts the design depends on, established from source, no code changes.

| Item | Output | Verification |
|---|---|---|
| 0.1 Extension-loading recon | Constraints list from `src/Core/ExtensionsManager.cs` + build graph at pin: how extension assemblies load, what cross-extension visibility exists today. Time-box: recon only, no design, no build. | Every constraint cited `path:line` at pin. Oracle probes two random citations for accuracy. |
| 0.2 MagicPrompt feature inventory | Table of user-facing features + backend surfaces of current upstream MagicPrompt (`../refs/SwarmUI-MagicPromptExtension` or fresh clone). This is the SPI sufficiency checklist for Stages 4–5. | Inventory cross-checked against MagicPrompt README and source; each row cites file. Falsification: a seeded fake feature row must be caught by the review pass. |
| 0.3 Transport internals ADR | One-page ADR: hand-rolled pipeline vs `openai-dotnet` protocol methods, decided against the 0.2 checklist (streaming, keyed backends, dialect control). The two memos from the 2026-07-04 session are the inputs. | ADR lists the losing option's strongest argument verbatim (steelman requirement). Oracle verdict on whether the decision follows from the cited evidence. |

Exit: three artifacts committed under `docs/`. No product code touched.

## Stage 1 — Hygiene completion (commit the current tree)

Goal: the working tree stops being a construction site.

Items:
1. Commit the justification purge + CLAUDE.md deletion currently sitting uncommitted on `fix/bad-code`.
2. Task #7: three constraint notes in vendor-native form (csproj one-liner; `ImageRejectionPattern` rewritten with `RegexOptions.IgnorePatternWhitespace` + `#` comments; `run-tests.sh` header citation to SwarmUI `docs/Making Extensions.md`).
3. Task #8: delete the dead `fetchModels` boot call in `Frontend/promptenhance.ts`; rebuild Assets.
4. Task #9: restore README connections column header to "Why" (SwarmUI extension standard 5).

Falsification probes: (a) hand-edit one emitted `Assets/*.js` byte → parity gate must fail; (b) x-mode regex rewrite: all 16 `LooksLikeImageRejection` cases must pass unchanged, and temporarily breaking one lookaround must fail the named case.

Exit: gates green on a clean tree; `/code-review` pass on the full branch diff; oracle disposition of any findings.

## Stage 2 — Contract becomes schema (codegen SSOT)

Goal: `contracts/pe-contract.json` stops being an arbitration file and becomes a consumed source of truth (task #6).

Items:
1. Node codegen script (no new deps) emits `Frontend/contracts.g.ts` constants and `PeContract.g.cs` from the JSON; generated files committed like `Assets/*.js`.
2. Parity gate becomes regenerate-and-diff; hand mirrors and their mirror-pinning tests deleted.
3. Close the `PE_LIMITS.maxTokens` missing-max hole.
4. Document the contract file as the platform's schema artifact (one paragraph in `docs/`).

Falsification probes: mutate one contract value → regenerate-and-diff gate fails; add an unmirrored key → generation surfaces it; revert both.

Exit: gates green; zero hand-typed contract literals outside generated files (rg sweep with positive control); oracle pass.

## Stage 3 — Canonical wire correctness

Goal: platform-grade behavior on spec-legal responses; a foundation must not ship silent truncation to consumers.

Items (each grounded in `openapi.yaml` at the refs mirror):
1. `finish_reason` handling: `length` and `content_filter` surfaced to the caller (new error/warning category or annotated success — decide in-stage), `stop` unchanged. (`openapi.yaml:43004` enum.)
2. `refusal` handling: `content: null` + `refusal` string returns the refusal text as a classified error, not `invalid_response_shape`. (`openapi.yaml:41155-41164`.)
3. Image MIME: stop defaulting to `image/jpeg` blind; sniff PNG/JPEG/WebP magic bytes in the browser adapter, keep jpeg as final fallback.
4. Dialect decision recorded: `max_tokens` vs `max_completion_tokens` strategy per the Stage 0.3 ADR (e.g., send `max_tokens` today; capability flag later).

Falsification probes: socket tests (`MockHttpServer`) serving a `length`-truncated response, a refusal response, and a PNG-labeled-jpeg request must each fail against pre-stage code and pass after.

Exit: gates green; every new behavior has a socket-level test; oracle pass scoped to spec conformance claims.

## Stage 4 — Transport SPI v1 + keyed backends

Goal: the seam other extensions will consume, shaped for capabilities before they are implemented.

Items:
1. SPI interface extraction: transport (list-models, chat, streaming-*shaped* signature even if v1 implements non-streaming only), settings persistence, error taxonomy — public, versioned, documented in `docs/SPI.md` with a compatibility policy (semver).
2. Auth: optional `apiKey` setting (9th knob → contract + codegen), `Authorization: Bearer` when set; investigate SwarmUI-native secret storage at pin before choosing where the key lives; rework the F5 error message + test.
3. Internals per the Stage 0.3 ADR.
4. SPI conformance test suite: the tests any implementation must pass — this is what a downstream consumer programs against.

Falsification probes: strip the Authorization header with the key set → keyed socket test fails; call the SPI from a test assembly that references only the public surface → compiles (internal-leak check: making one SPI type internal must break that test).

Exit: gates green; SPI docs published in-repo; oracle pass with explicit API-sufficiency check against the Stage 0.2 checklist (every MagicPrompt feature mappable to an SPI call or explicitly deferred with a named gap).

## Stage 5 — Validation by consumption (compile-time composition; no core changes)

Goal: the strangler-fig proof, executed without touching SwarmUI core.

Items:
1. Fork MagicPrompt into a working tree; strip duplicated infrastructure (transport, settings persistence, error handling) per its own KEEP/DELETE pass.
2. Re-platform it as a thin feature extension consuming the foundation SPI. Composition mechanism (recon constraints 20/22/30: a bare cross-extension reference cannot resolve at runtime, and a hand-copied DLL splits type identity): the consumer ships the foundation as a private dependency in its own build output while the foundation's own extension folder is suffixed `.disable` (recon constraint 5) — one copy, one identity, zero core changes. Recorded nuance: as a private dep the foundation's `Extension` lifecycle does not run, so Stage 5 proves SPI sufficiency as a library; live instance-sharing between independently loaded extensions remains Stage 6's charter.
3. Feature-parity run against the Stage 0.2 inventory: every row demonstrated or dispositioned.
4. Record every point where the SPI was insufficient or awkward → SPI v1.x changes fed back to Stage 4 artifacts.
5. Disposition the settings-persistence behavior change deliberately: MagicPrompt persists instance-globally (`GenericSharedUser`, inventory B4) while the foundation is per-user — the migration flips user-visible behavior, and the parity checklist must record it as intended, not discover it.

Falsification probes: delete one SPI method the consumer uses → consumer build fails (proves real consumption, not vendored copies); `just vendor-ci-test` with both extensions loaded must pass, and must fail when the consumer references a foundation-internal type.

Exit: both extensions boot in the real host with zero logged errors; parity checklist complete; oracle pass on the sufficiency claims. This stage is the product-validation milestone.

## Stage 6 — Host-level dependency seam (deferred integration; upstream PRs)

Entry condition: Stage 5 complete and validated. Not before.

Items:
1. Design the extension-dependency mechanism against the Stage 0.1 constraints (load order, assembly identity, discovery).
2. Fork SwarmUI; implement; PR upstream per standard 4 (no core hacking — fix core properly instead).
3. Convert the Stage 5 compile-time composition to the host mechanism; re-run the Stage 5 probe suite unchanged — the SPI design rule above is validated precisely here (no SPI signature may need to change).

Falsification probe: the Stage 5 conformance suite runs green against the loader-based composition with zero SPI diffs; any required SPI change is a CONFIRMED design-rule violation and goes back to Stage 4.

Exit: upstream PR open (merge timing is mcmonkey's); both composition modes green.

## Stage 7 — Publication

Entry condition: owner declares the repo fit for human eyes. Not before.

Items:
1. Extension-author guide (`docs/`): golden path walkthrough — the teaching layer the code comments deliberately do not carry.
2. Extension list PR to `launchtools/extension_list.fds`.
3. MagicPrompt upstream PR (the strangler-fig, now on the host mechanism) with the feature-parity evidence attached.

Exit: listings live; downstream PR open; final oracle pass on all published documents (claims vs evidence).

## Task map

| Stage | Existing tasks | New tasks |
|---|---|---|
| 0 | — | recon, inventory, ADR |
| 1 | #7, #8, #9 | commit purge |
| 2 | #6 | — |
| 3 | — | finish_reason, refusal, MIME, dialect ADR row |
| 4 | — | SPI v1, auth, conformance suite |
| 5 | — | strangler-fig composition |
| 6 | — | loader + upstream PR (deferred) |
| 7 | — | guide, listings, MagicPrompt PR (gated on owner) |

Stages are strictly ordered except: Stage 2 and Stage 3 may run in parallel after Stage 1; everything else is sequential.
