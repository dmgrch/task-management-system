# Context Engineering — static comparison

The checks below are deterministic **source-code heuristics**. A failure
is a *flag for review*, not necessarily a proven runtime defect; a pass
does not prove functional correctness. Build/test statuses are read from
saved `*_EXIT` markers in log files and are never guessed.

## Summary

| Run | Static rule flags (of 7) | Build | Tests |
|---|---:|---|---|
| baseline-1 | 2 | pass | pass |
| baseline-2 | 1 | pass | pass |
| baseline-3 | 1 | pass | pass |
| context-1 | 0 | pass | pass |
| context-2 | 0 | pass | pass |
| context-3 | 0 | pass | pass |

## Test-source changes (not a pass/fail assessment)

| Run | Modified test files |
|---|---:|
| baseline-1 | 1 |
| baseline-2 | 1 |
| baseline-3 | 1 |
| context-1 | 1 |
| context-2 | 2 |
| context-3 | 1 |

## Per-rule results

| Rule | baseline-1 | baseline-2 | baseline-3 | context-1 | context-2 | context-3 |
|---|---|---|---|---|---|---|
| HTTP → service → repository for the new task-list behavior | FLAG | FLAG | FLAG | PASS | PASS | PASS |
| Task-list route retains task-view authorization | PASS | PASS | PASS | PASS | PASS | PASS |
| Task-list query excludes soft-deleted tasks | PASS | PASS | PASS | PASS | PASS | PASS |
| Paged response is a typed DTO with TaskResponse items | PASS | PASS | PASS | PASS | PASS | PASS |
| Added validation uses VALIDATION_ERROR and HTTP 400 | PASS | PASS | PASS | PASS | PASS | PASS |
| Request cancellation token is propagated | PASS | PASS | PASS | PASS | PASS | PASS |
| Updated OpenAPI describes pagination and filter parameters | FLAG | PASS | PASS | PASS | PASS | PASS |

## Group means

- Baseline: 1.33 static flags per attempt
- Context: 0.00 static flags per attempt

## Scope and limitations

- Task-history convention is **N/A** for this read-only endpoint; it is not scored.
- The OpenAPI check is a supplementary API workflow convention.
- The checker never executes code and cannot guarantee correct pagination,
  `totalCount`, permissions, HTTP status mapping, or cancellation propagation.
- The task-list method parser is best-effort; unusual C# syntax may produce
  a false flag. Review flagged cases against the committed diff.
- Do not compare raw line/file counts as quality metrics.
- For a defensible functional score, also run the **same independent tests**
  on each attempt and record their results separately.
