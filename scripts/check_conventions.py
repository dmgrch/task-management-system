#!/usr/bin/env python3
"""Reproducible static checks for the Context Engineering laboratory.

Run in the Task Management System Git repository (Python 3.9+, stdlib only).
The script reads committed branch content with `git show`; it never checks out or
modifies experiment branches. Counts are static rule flags, not proof that every
API behavior is correct. Build/test outcomes come only from saved log exit codes.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

BASE = "lab-base"
CONTEXT_BASE = "context-setup"
RUNS = [
    ("baseline-1", "baseline-attempt-1", BASE, "attempt-1"),
    ("baseline-2", "baseline-attempt-2", BASE, "attempt-2"),
    ("baseline-3", "baseline-attempt-3", BASE, "attempt-3"),
    ("context-1", "context-attempt-1", CONTEXT_BASE, "context-attempt-1"),
    ("context-2", "context-attempt-2", CONTEXT_BASE, "context-attempt-2"),
    ("context-3", "context-attempt-3", CONTEXT_BASE, "context-attempt-3"),
]
ROOT = "services/ProjectTaskService"
CONTROLLER = f"{ROOT}/Controllers/ProjectsController.cs"
SERVICE = f"{ROOT}/Services/TaskService.cs"
ISERVICE = f"{ROOT}/Services/ITaskService.cs"
REPO = f"{ROOT}/Repositories/TaskRepository.cs"
IREPO = f"{ROOT}/Repositories/ITaskRepository.cs"
OPENAPI = "docs/openapi/project-task-service.openapi.yaml"
RULES = [
    ("architecture", "HTTP → service → repository for the new task-list behavior"),
    ("authorization", "Task-list route retains task-view authorization"),
    ("soft_delete", "Task-list query excludes soft-deleted tasks"),
    ("typed_dto", "Paged response is a typed DTO with TaskResponse items"),
    ("validation_errors", "Added validation uses VALIDATION_ERROR and HTTP 400"),
    ("cancellation", "Request cancellation token is propagated"),
    ("openapi", "Updated OpenAPI describes pagination and filter parameters"),
]


def git(*args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(["git", *args], text=True, capture_output=True, check=check)


def git_out(*args: str) -> str:
    return git(*args).stdout.strip()


def present(ref: str) -> bool:
    return git("rev-parse", "--verify", "--quiet", f"{ref}^{{commit}}", check=False).returncode == 0


def show(ref: str, filename: str) -> str:
    p = git("show", f"{ref}:{filename}", check=False)
    return p.stdout if p.returncode == 0 else ""


def changed_files(base: str, ref: str) -> set[str]:
    return set(filter(None, git_out("diff", "--name-only", base, ref).splitlines()))


def additions(base: str, ref: str, filename: str) -> str:
    """Only new lines, without file header markers, so old code isn't scored as new."""
    result = git("diff", "--no-ext-diff", "--unified=0", base, ref, "--", filename)
    return "\n".join(
        line[1:] for line in result.stdout.splitlines()
        if line.startswith("+") and not line.startswith("+++")
    )


def method_body(source: str, method_name: str) -> str:
    """Best-effort extraction of a C# brace-bodied method."""
    m = re.search(r"\b" + re.escape(method_name) + r"\s*\(", source)
    if not m:
        return ""
    start = source.find("{", m.end())
    if start < 0:
        return ""
    depth = 0
    for i in range(start, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[m.start():i + 1]
    return ""


def log_exit(evidence: Path, name: str, kind: str) -> str:
    path = evidence / f"{name}-{kind}.log"
    if not path.is_file():
        return "missing"
    data = path.read_text(encoding="utf-8", errors="replace")
    marker = "BUILD_EXIT" if kind == "build" else "TEST_EXIT"
    vals = re.findall(r"(?m)^" + marker + r"=(\d+)\s*$", data)
    if not vals:
        return "unknown"
    return "pass" if int(vals[-1]) == 0 else "fail"


def check_run(ref: str, base: str, evidence: Path, log_name: str) -> dict[str, Any]:
    changed = changed_files(base, ref)
    controller = show(ref, CONTROLLER)
    service = show(ref, SERVICE)
    repository = show(ref, REPO)
    doc = show(ref, OPENAPI)
    controller_add = additions(base, ref, CONTROLLER)
    service_add = additions(base, ref, SERVICE)
    repo_add = additions(base, ref, REPO)
    method = method_body(controller, "GetTasksByProject")
    dto_paths = sorted(p for p in changed if re.search(r"/Dtos/PagedTasks?Response\.cs$", p))
    dto_text = "\n".join(show(ref, p) for p in dto_paths)

    # We check the new task-list behavior, not the legacy project's DB lookups.
    fields = re.findall(r"\bITaskService\s+(\w+)\s*;", controller)
    invokes_task_service = any(re.search(r"\b" + re.escape(field) + r"\s*\.", method)
                               for field in fields)
    no_direct_task_db = not re.search(r"\b_dbContext\.Tasks\b|\b_db\.Tasks\b", method)
    layers_changed = all(p in changed for p in (SERVICE, ISERVICE, REPO, IREPO))
    architecture = bool(method and invokes_task_service and no_direct_task_db and layers_changed)

    # Authorization may be implemented in the controller or delegated to the service.
    auth_in_controller = bool(re.search(r"ProjectAction\.ViewTask|ValidateAccessAsync\s*\(", method))
    auth_in_new_service = "ProjectAction.ViewTask" in service_add
    authorization = bool(auth_in_controller or auth_in_new_service)

    # For the baseline the filter resides in the controller. With context, it
    # should be in the ADDED repository query, not only an old unrelated method.
    is_deleted_expr = r"!\s*\w+\.IsDeleted\b|\w+\.IsDeleted\s*==\s*false\b|\w+\.IsDeleted\s*!=\s*true\b"
    soft_delete = bool(re.search(is_deleted_expr, method) or re.search(is_deleted_expr, repo_add))

    typed_dto = bool(dto_paths and re.search(r"\bTaskResponse\b", dto_text)
        and re.search(r"\bItems\s*\{", dto_text))

    # Search newly introduced validation statements; not old unrelated errors.
    validation_add = controller_add + "\n" + service_add
    validation_errors = bool("VALIDATION_ERROR" in validation_add
        and ("BadRequest(" in validation_add or "TaskErrorKind.Validation" in validation_add
             or "StatusCodes.Status400" in validation_add or "400" in validation_add))

    # When layers are new, require tokens in both the new service and repository
    # APIs; baseline controller-only approach can still propagate its token.
    if layers_changed:
        cancellation = bool("CancellationToken" in method
            and "CancellationToken" in service_add and "CancellationToken" in repo_add)
    else:
        cancellation = bool("CancellationToken" in method
            and re.search(r"(?:ToListAsync|CountAsync|FirstOrDefaultAsync|AnyAsync)\s*\([^;]*?cancellationToken", method, re.S))

    docs_added = additions(base, ref, OPENAPI)
    openapi = bool(OPENAPI in changed
        and all(re.search(r"\bname\s*:\s*" + p + r"\b", doc, re.I)
                for p in ("page", "limit", "status"))
        and ("PagedTask" in docs_added or "totalCount" in docs_added or "totalPages" in docs_added))

    rule_result = {
        "architecture": architecture,
        "authorization": authorization,
        "soft_delete": soft_delete,
        "typed_dto": typed_dto,
        "validation_errors": validation_errors,
        "cancellation": cancellation,
        "openapi": openapi,
    }
    return {
        "ref": ref,
        "base": base,
        "commit": git_out("rev-parse", ref),
        "changed_files": sorted(changed),
        "rules": rule_result,
        "static_flags": sum(1 for v in rule_result.values() if not v),
        "test_files_changed": sorted(p for p in changed if p.startswith("tests/") and p.endswith(".cs")),
        "build": log_exit(evidence, log_name, "build"),
        "test": log_exit(evidence, log_name, "tests"),
    }


def markdown_report(results: dict[str, Any]) -> str:
    rows = [
        "# Context Engineering — static comparison",
        "",
        "The checks below are deterministic **source-code heuristics**. A failure",
        "is a *flag for review*, not necessarily a proven runtime defect; a pass",
        "does not prove functional correctness. Build/test statuses are read from",
        "saved `*_EXIT` markers in log files and are never guessed.",
        "",
        "## Summary",
        "",
        "| Run | Static rule flags (of 7) | Build | Tests |",
        "|---|---:|---|---|",
    ]
    for label, _, _, _ in RUNS:
        r = results["runs"][label]
        rows.append(f'| {label} | {r["static_flags"]} | {r["build"]} | {r["test"]} |')
    rows.extend([
        "", "## Test-source changes (not a pass/fail assessment)", "",
        "| Run | Modified test files |",
        "|---|---:|",
    ])
    for label, *_ in RUNS:
        r = results["runs"][label]
        rows.append(f'| {label} | {len(r["test_files_changed"])} |')
    rows.extend(["", "## Per-rule results", ""])
    rows.append("| Rule | " + " | ".join(label for label, *_ in RUNS) + " |")
    rows.append("|---|" + "---|" * len(RUNS))
    for key, title in RULES:
        cells = ["PASS" if results["runs"][label]["rules"][key] else "FLAG"
                 for label, *_ in RUNS]
        rows.append(f'| {title} | ' + " | ".join(cells) + " |")
    baselines = [results["runs"][f"baseline-{i}"]["static_flags"] for i in (1, 2, 3)]
    contexts = [results["runs"][f"context-{i}"]["static_flags"] for i in (1, 2, 3)]
    rows.extend([
        "", "## Group means", "",
        f"- Baseline: {sum(baselines) / 3:.2f} static flags per attempt",
        f"- Context: {sum(contexts) / 3:.2f} static flags per attempt",
        "", "## Scope and limitations", "",
        "- Task-history convention is **N/A** for this read-only endpoint; it is not scored.",
        "- The OpenAPI check is a supplementary API workflow convention.",
        "- The checker never executes code and cannot guarantee correct pagination,",
        "  `totalCount`, permissions, HTTP status mapping, or cancellation propagation.",
        "- The task-list method parser is best-effort; unusual C# syntax may produce",
        "  a false flag. Review flagged cases against the committed diff.",
        "- Do not compare raw line/file counts as quality metrics.",
        "- For a defensible functional score, also run the **same independent tests**",
        "  on each attempt and record their results separately.",
        "",
    ])
    return "\n".join(rows)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence-dir", type=Path, default=Path("../lab-evidence"),
                        help="Folder with saved build/test logs (default: ../lab-evidence)")
    parser.add_argument("--out-dir", type=Path, default=Path("../lab-evidence"),
                        help="Where to write analysis JSON/Markdown (default: ../lab-evidence)")
    args = parser.parse_args()

    if git("rev-parse", "--show-toplevel", check=False).returncode != 0:
        print("ERROR: Run this script inside the Git repository.", file=sys.stderr)
        return 2
    missing = [r for r in (BASE, CONTEXT_BASE, *(t[1] for t in RUNS)) if not present(r)]
    if missing:
        print("ERROR: Missing local Git refs: " + ", ".join(missing), file=sys.stderr)
        return 2

    # Ensure each attempt is based on its intended immutable starting point.
    context_changes = changed_files(BASE, CONTEXT_BASE)
    expected_context = {
        "AGENTS.md",
        ".agents/skills/implement-api-endpoint/SKILL.md",
    }
    if context_changes != expected_context:
        print("WARNING: context-setup differs from lab-base in unexpected files: "
              + ", ".join(sorted(context_changes ^ expected_context)), file=sys.stderr)

    for label, ref, base, _ in RUNS:
        common = git_out("merge-base", ref, base)
        if common != git_out("rev-parse", base):
            print(f"ERROR: {label} is not descended from {base}.", file=sys.stderr)
            return 2
        if label.startswith("baseline") and git_out("merge-base", ref, BASE) != git_out("rev-parse", BASE):
            print(f"ERROR: {label} has unexpected base.", file=sys.stderr)
            return 2

    data: dict[str, Any] = {
        "generated_utc": datetime.now(timezone.utc).isoformat(),
        "method": "static source heuristics; flags are not verified runtime failures",
        "rules": {key: description for key, description in RULES},
        "runs": {},
    }
    for label, ref, base, log_name in RUNS:
        data["runs"][label] = check_run(ref, base, args.evidence_dir, log_name)

    args.out_dir.mkdir(parents=True, exist_ok=True)
    jfile = args.out_dir / "context-check-results.json"
    mdfile = args.out_dir / "context-check-report.md"
    jfile.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    mdfile.write_text(markdown_report(data), encoding="utf-8")
    print(markdown_report(data))
    print(f"Saved: {jfile}\nSaved: {mdfile}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
