# Vasquez — History

## Seed (2026-07-10)

- **Project:** lucia-dotnet — privacy-first multi-agent AI assistant for Home Assistant.
- **Stack:** .NET 10 / C# 14, Aspire 13, Ollama/Azure OpenAI, xUnit + FakeItEasy, TypeScript/React dashboard, Python HA custom component, Docker/K8s.
- **Owner:** Zack Way.
- **Why I exist:** Owner hired me as the team's PR Review Gatekeeper. I review every `squad/*` worktree branch before it is pushed or turned into a PR, and I hold the merge gate — nothing reaches `master` until I've reviewed the diff and all blocking problems are resolved.
- **My model is fixed to `gpt-5.6-sol`** by owner mandate. No fallback.
- **The hard gate:** the version-controlled `.githooks/pre-push` hook (active via `core.hooksPath=.githooks`, installed by `scripts/install-git-hooks.sh`) blocks any push whose destination is `refs/heads/squad/*` unless the pushed SHA has an approval marker in `<git-common-dir>/squad-approvals/<sha>`. The hook runs the gate and then the stock Git LFS step. I write that marker only after a clean review, via `.squad/gate/Approve-Branch.ps1`.

## Learnings

- Repo constitution non-negotiables to enforce at the gate: ONE class per `.cs` file; TDD/tests for public behavior; nullable reference types; file-scoped namespaces; `[LoggerMessage]` logging; OpenTelemetry retained; Conventional Commits + `Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>` trailer with `Fixes #N` footer.
- Known non-blocking baseline: ~141 eval tests fail without LLM/Ollama backends — that is pre-existing, not a branch regression.
- Merge reality in this repo: Copilot's automated review only ever leaves COMMENTED reviews (never APPROVED), and branch protection requires all conversations resolved even with `--admin`. My APPROVE is the team's human-equivalent gate before that machinery.

## Reviews

### 2026-07-10 — PR #221 / `squad/163-utc-timestamps`

- **Verdict:** APPROVE at `80a69e615437407d1dc5e2ee556557185579ab23`; approval marker confirmed.
- Scope matches #163. The nine applied-config checkpoint changes only replace local clocks with UTC; the separate lost-update race remains untouched and is tracked by #225.
- Verified DateTime-kind-specific filter normalization, `DateTimeOffset.TryParse` with valid styles, canonical UTC writes/reads, 500-row keyset batches, prepared UPDATE reuse, batch savepoints, and outer-transaction rollback safety.
- `lucia.Data` build: 0 warnings, 0 errors. SQLite-filtered `lucia.Tests`: 44 passed, 0 failed.

## 2026-07-10 — PR #220 (`squad/178-stt-semaphore-scope`)

- **Verdict:** REQUEST-CHANGES at `3fce3715485c6f910f7d83c940e95a87901e068a`; no approval marker written.
- **Validation:** `lucia.Wyoming` build succeeded with 0 warnings/0 errors. Wyoming-filtered tests: 299 passed, 10 skipped, 0 failed.
- **Blocking correctness:** Concurrent sync/async teardown can still release the STT permit before `HybridSttSession.DisposeAsync()` finishes awaiting its pending transcription. Its `_disposed` early-return is not completion coordination: a second disposer returns immediately and can win the atomic permit release.
- **Blocking correctness:** Teardown can release while an already-running `GetFinalResultAsync()` performs final synchronous inference because `DisposeAsync()` tracks only `_pendingTranscription`, not active finalization.
- **Blocking balance:** Both STT disposal helpers release outside `finally`; any custom session disposal failure leaks the permit.
- **Documentation:** The `HybridSttSession` type summary still says only `GetFinalResult()` waits, but `DisposeAsync()` now waits too and the actual method is `GetFinalResultAsync()`.
- **Durable learning:** `Interlocked.Exchange` makes permit release single-shot, but does not prove the winning caller waited for inference. Idempotent async disposal must expose one shared completion that every concurrent disposer awaits, and permit release belongs in a `finally`.

## 2026-07-10 — PR #220 re-review at `1732bcf1`

- **Verdict:** REQUEST-CHANGES; no approval marker written.
- **Validation:** `lucia.Wyoming` build succeeded with 0 warnings/0 errors. Wyoming-filtered tests: 300 passed, 10 skipped, 0 failed.
- The `Lazy<Task>` with `ExecutionAndPublication` correctly gives concurrent disposal callers one shared disposal completion, and both WyomingSession helpers now release in `finally` through the atomic release gate.
- **Remaining blocker:** `DisposeCore` awaits only `_pendingTranscription`. An already-running `GetFinalResultAsync` resumes after that task and performs another synchronous `RunTranscription`, so synchronous teardown can still return and release the permit while final inference runs.
- The new race test completes its disposal task from the same gate that merely allows `GetFinalResultAsync` to resume, then waits for `runTask` before checking the final count. It therefore cannot detect the early-release window it claims to cover.
- **Additional race:** `AcceptAudioChunk` can pass its disposed check before `DisposeCore`, then publish/start `_pendingTranscription` after `DisposeCore` has observed null and returned.
- **Durable learning:** Sharing a prerequisite task is not the same as sharing the complete operation task. A teardown waiter must await the actual active finalization/ingestion operation, and a regression fake needs a second gate after its prerequisite to expose premature release.

## 2026-07-10 — PR #220 re-review at `cd2de939`

- **Verdict:** REQUEST-CHANGES; no approval marker written.
- **Validation:** `lucia.Wyoming` build succeeded with 0 warnings/0 errors. Wyoming-filtered tests: 300 passed, 10 skipped, 0 failed.
- **Resolved:** `_disposeLock` correctly serializes disposal against finalization startup; disposal awaits the complete `_finalizationTask`; the strengthened test exposes early release during its final-inference gate; the class summary matches that mechanism. No sync-over-async deadlock was found because no lock is held across an `await` or blocking wait.
- **Remaining blocking race:** `AcceptAudioChunk` does not participate in `_disposeLock`. It can pass the volatile disposed check, then `DisposeCore` can set disposed, observe both finalization and pending transcription as null, and return; afterward `AcceptAudioChunk` can publish/start `_pendingTranscription`, causing inference after the permit was released.
- **Durable learning:** A dispose/finalize lock must also serialize every producer that can create newly awaited work; otherwise teardown can safely await the current task yet miss work published immediately afterward.

## 2026-07-10 — PR #220 re-review at `5ec0ba88`

- **Verdict:** REQUEST-CHANGES; no approval marker written.
- **Validation:** `lucia.Wyoming` build succeeded with 0 warnings/0 errors. Wyoming-filtered tests: 301 passed, 10 skipped, 0 failed.
- **Production fix verified:** the disposed re-check and sole `_pendingTranscription` publication are atomic under `_disposeLock`; `_finalizationTask` publication is under the same lock; buffer locks are released before acquiring the disposal lock; no task is awaited while holding it. Prior finalization and exactly-once release fixes remain intact.
- **Blocking test defect:** the regression test injects `GatableAcceptSttSession`, which independently reimplements the desired lock. Removing the production `HybridSttSession` lock would not fail this test. It also uses a 100 ms delay instead of awaiting deterministic acceptance completion, so a delayed worker can produce a false pass.
- **Documentation defect:** `Task.Run` does not guarantee its callback starts only after the caller releases `_disposeLock`; a pool thread may begin ONNX inference before `Task.Run` returns or before `Monitor.Exit`. The comments' claim that inference runs entirely outside the critical section is inaccurate, although the permit invariant remains safe because disposal cannot acquire the lock before task publication.
- **Durable learning:** A regression test that duplicates production synchronization in a fake validates the fake, not production. Test seams must exercise the production synchronization point, and completion must be signaled rather than inferred from delay.

## 2026-07-10 — PR #220 re-review at `3c8423d2`

- **Verdict:** REQUEST-CHANGES; no approval marker written.
- **Validation:** `lucia.Wyoming` build succeeded with 0 warnings/0 errors. Wyoming-filtered tests: 301 passed, 10 skipped, 0 failed; the new disposal-race test passed independently.
- **Resolved:** the deterministic TCS/event-gate test now exercises the real `HybridSttSession` guard at the exact pre-lock seam; there is no arbitrary delay. The corrected `Task.Run` documentation accurately allows worker execution before monitor exit while preserving publication-before-disposal. Prior finalization and permit invariants remain intact.
- **Constitution blocker:** `HybridSttSession.cs` now declares both `HybridSttSession` and nested `ForTestOnly`. The gate's explicit one-type-per-C#-file rule rejects this even though one declaration is a private tag struct. Use a primitive constructor discriminator or move the tag to its own file.
- **Durable learning:** Test-only constructor disambiguation must still obey the one-type-per-file constitution; nullable annotations do not distinguish overloads at runtime, but a primitive tag avoids adding a nested type.

## 2026-07-10 — PR #220 final approval at `e5e59f8c`

- **Verdict:** APPROVE for exact HEAD `e5e59f8c3ad76a6e3e782ca399ae5b5dfe9dc8aa`.
- **Validation:** `lucia.Wyoming` build succeeded with 0 warnings/0 errors. Wyoming-filtered tests: 301 passed, 10 skipped, 0 failed.
- The primitive constructor discriminator removes the nested type without changing the public production path. The real-session deterministic race test and internal seams remain correctly wired.
- Final concurrency invariants verified: atomic progressive publication versus disposal; complete progressive/final inference awaiting; atomic exactly-once permit release in `finally`; detection response before acquisition; release before unrelated save tasks; no lock-order inversion or sync-over-async deadlock.

## PR #224 Review (2026-07-10)

- **Branch / HEAD:** `squad/140-httpclient-lifetime` / `8e8c55cc64cb397294b101129b25bd1e45360692`.
- **Verdict:** REQUEST-CHANGES; no approval marker written.
- **Verified:** typed `request.Headers.Authorization` overwrite; blank-token clear; central AgentHost, A2AHost, and `AddHomeAssistant` handler chains; no `ParameterSweepRunner.cs` branch diff; one type per touched C# file; merge-tree with current `origin/master` is conflict-free.
- **Validation:** `lucia.HomeAssistant`, `lucia.A2AHost`, and `lucia.EvalHarness` builds succeeded with 0 warnings/errors; all 7 `HomeAssistantAuthorizationHandlerTests` passed.
- **Blockers:** acquired eval clients remain unowned if construction throws before `_instances.Add`; the dynamic-agent catch directly disposes a client already owned by a tracked instance after `InitializeAsync` failure, allowing later double-disposal; several test `AddHttpClient<HomeAssistantClient>` paths still use static `DefaultRequestHeaders.Authorization`; no disposal/failure-path regression tests cover the new ownership contract.
- **Learning:** resource ownership must transfer immediately after acquisition, and all cleanup must pass through the idempotent owner; auditing only shared extension methods misses one-off test/host registrations.

### 2026-07-10 — PR #224 re-review / `squad/140-httpclient-lifetime`

- **HEAD:** `85e4d4bb3cfc4c2e350f6b001ba53db2fd1c5d28` (local, one commit ahead of remote).
- **Verdict:** REQUEST-CHANGES; no approval marker written.
- **Resolved from prior review:** all seven factory methods now guard pre-transfer cleanup with `tracked`; tracked initialization failures no longer hand-dispose; the three cited test DI paths install `HomeAssistantAuthorizationHandler`; one type per new/changed C# file.
- **Validation:** four affected projects built with 0 warnings/errors; auth tests 7/7 and local eval-harness tests 2/2 passed; `ParameterSweepRunner.cs` remains untouched.
- **Blocking:** current `origin/master` already contains `lucia.EvalHarness.Tests` from PR #223, so the new csproj has an add/add merge conflict and drops master-specific test settings; Squad CI builds the solution but executes only `lucia.Tests`, so the two new tests never run; the claimed factory-failure regression test manually loops a local list and never exercises `RealAgentFactory`, so it cannot detect a tracked-flag regression.
- **Non-blocking:** `HomeAssistantErrorHandlingTests.cs` was converted wholesale from CRLF to LF, inflating the commit by roughly 300 lines of unrelated churn.
- **Learning:** compile inclusion is not test execution; new test projects need an explicit CI test command. A regression test must invoke the production path it claims to protect, not restate that path in test-only code.

### 2026-07-10 — PR #224 final re-review / `squad/140-httpclient-lifetime`

- **HEAD:** `c02743670055acc3ec40c7b71761fd6ee6e15995`.
- **Verdict:** APPROVE; exact-SHA approval marker written and confirmed.
- Rebase is clean against `origin/master`; the existing PR #223 `lucia.EvalHarness.Tests` project remains singular, retains both parameter-sweep test files/settings, and now includes the new ownership tests. The solution references it once.
- Squad CI explicitly builds the solution and runs `dotnet test lucia.EvalHarness.Tests` in Release configuration.
- `ChatClientCreator` defaults to the real `BackendChatClientFactory.CreateChatClient`; the regression test invokes real `RealAgentFactory.CreateDynamicAgentAsync`, verifies pre-transfer failure disposes once, then verifies factory teardown does not double-dispose.
- **Validation:** Release solution build succeeded with 0 warnings/errors; auth-handler tests 7/7 passed; EvalHarness tests 32/32 passed. `ParameterSweepRunner.cs` is untouched; one type per changed C# file; commit metadata is compliant.
- **Non-blocking:** the committed `HomeAssistantErrorHandlingTests.cs` blob still changes CRLF to LF, so the PR diff shows whole-file newline churn despite only 22 additions/6 removals when CR is ignored.
