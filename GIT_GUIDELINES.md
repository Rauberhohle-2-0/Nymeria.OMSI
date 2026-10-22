# Git guidelines

These rules apply to every change in Nymeria.OMSI. They exist so history stays
small, reviewable, and truthful, and so the pinned engine dependency stays
reproducible. See [AGENTS.md](AGENTS.md) for agent-specific conduct and
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the engine ownership boundary.

## Branches

`main` is the default development branch and the only branch implementation work is
performed on. This repository has a single maintainer and a task-driven workflow, so
a branch plus pull request for every task costs time and adds nothing.

- Perform every implementation task directly on `main`.
- Before implementing, verify the branch. `git branch --show-current` must print
  `main`.
- If the current branch is not `main`, do not create a branch and do not switch
  branches automatically. Report the branch, what it contains, and any uncommitted
  changes, then request instructions.
- Do not create feature, task, or temporary development branches unless the project
  owner explicitly requests one.
- Do not open pull requests automatically. Create one only when the owner asks.
- Do not merge, rebase, delete, or push branches without authorization.
- Never force-push and never rewrite published history. Correct a faulty published
  change with a new revert or fix commit.

Unmerged implementation work on another branch is reported before any further work.
It is never merged, deleted, or rewritten on the agent's own initiative.

## Default workflow

Start from a current `main`:

```bash
git status
git branch --show-current
git pull --ff-only origin main
```

`--ff-only` is deliberate: it cannot create a merge commit, and it cannot rebase or
otherwise alter local work. If it refuses, local `main` and `origin/main` have
diverged; stop and report that instead of merging, resetting, or force-pushing.

Implement the requested change, then run the validation the task allows and report
the results ([AGENTS.md](AGENTS.md) section 7,
[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)).

```bash
git add <explicitly selected files>
git commit -m "feat: describe the implemented change"
git push origin main
```

- Staging is explicit: name every path, never `git add -A`.
- The commit message must describe the change that was actually made. A subject that
  does not match the diff is a defect, not a wording preference.
- `git add` and `git commit` belong to the task. `git push origin main` needs
  explicit authorization from the project owner or from the current task; commit
  locally and stop when that authorization is missing.

## Prohibited behavior

- Automatic feature branches, task branches, or temporary development branches.
- Automatic pull requests.
- Unnecessary branch switching.
- Force pushes.
- Rewriting history.
- `Co-authored-by` trailers identifying AI assistants, and any other AI attribution
  in commit messages.
- Committing generated binaries, local OMSI assets, or build artifacts.
- Modifying the Nymeria engine submodule unless explicitly requested.

## Commits

- One small, logically complete change per commit.
- Imperative, descriptive subject with a short type prefix, for example
  `chore: bootstrap Nymeria.OMSI host`, `ci: add platform build checks`,
  `docs: record engine integration decision`, `fix: correct SDL3 runtime message`.
- Avoid subjects such as `update`, `changes`, or `wip` on their own. Use the body to
  explain the reason or trade-off when it is not obvious.
- Stage explicit paths (`git add docs/ARCHITECTURE.md`). Never `git add -A`.
- Before committing, review `git status`, `git diff`, and `git diff --staged`.
- Never commit:
  - build output (`bin/`, `obj/`, `artifacts/`, logs, caches),
  - generated shader bytecode such as `*.dxil`,
  - downloaded toolchains or native libraries,
  - local credentials, `.env` files, tokens, or private keys,
  - machine-specific paths or editor/OS state,
  - a locally moved engine pointer that was not reviewed as an upgrade.

## Commit attribution

- No `Co-authored-by` trailers, including for AI tools or assistants.
- No `Generated-by`, `Assisted-by`, or similar footer, and no automated insertion
  of AI credit into commits, pull request descriptions, or changelogs.
- Preserve truthful author and committer metadata. Do not change Git identity and
  do not add the agent as an author or committer.
- Do not rewrite historical commits to remove attribution that already exists.
  Fix forward.

## Scoped attribution check

[scripts/check-commit-trailers.sh](scripts/check-commit-trailers.sh) rejects
attribution trailers that a change **introduces**. It never edits history.

```bash
scripts/check-commit-trailers.sh origin/main..HEAD   # local change
scripts/check-commit-trailers.sh --head              # single commit (first push)
```

What it scans, and why that range is safe:

- It reads commit messages of the superproject commits in the given range only.
- It never scans engine history: submodule history is a separate repository that
  the superproject's `git rev-list` does not traverse. The engine's own commits are
  not this repository's responsibility and are not affected by this rule.
- It requires a full (non-shallow) checkout. A shallow clone cannot resolve the
  range completely, so the script fails with an explanation instead of silently
  under-reporting.
- In `.github/workflows/ci.yml`, the `commit-policy` job checks out with
  `fetch-depth: 0` for that reason, and does **not** initialize submodules.
- On `pull_request`, the checked range starts at
  `git merge-base <base.sha> <head.sha>`, so commits that already exist on the base
  branch are never attributed to the pull request.
- On `push`, the checked range is `before..after`, i.e. exactly the commits the push
  introduced. A first push to a branch has no parent range and is checked as a
  single commit.

The check flags `Co-authored-by`, `Co-authored by`, `Generated-by`,
`Generated by`, `Assisted-by`, and `Assisted by` trailer lines. Maintaining a
human co-author relationship, if ever needed, is a maintainer decision made in the
commit author line or pull request description, not through a trailer.

## Engine dependency changes

- The engine is pinned by submodule commit. A pointer change **is** an engine
  upgrade.
- An upgrade is its own pull request: state the old and new commit hashes, the
  reason, any upstream changes relied on, and the verification performed.
- Never move the pointer as a side effect of unrelated work, and never use
  `git submodule update --remote` or a branch name as a build input.
- Do not modify files under `external/Nymeria/`. If the engine is wrong, open a
  change in the Nymeria repository.

## Review of direct commits

Changes land on `main` as reviewed commits, not through a pull request per task.

- Review your own change before committing: `git status`, `git diff`, and
  `git diff --staged`. A direct commit does not remove the review step; it moves the
  review to before the commit instead of after it.
- The commit-policy and platform build jobs in
  [.github/workflows/ci.yml](.github/workflows/ci.yml) still run on every push to
  `main` and on pull requests targeting `main`, so a pushed commit stays verifiable.
- A pull request is optional and is created only when the owner asks for one. When
  one exists, fill in the pull request template: scope, platform validation results,
  dependency changes, and Windows validation status.
- Merge commits are not part of the workflow. Existing merge commits in history are
  left as they are.

### Repository settings that committed files cannot enforce

GitHub branch protection or rulesets are repository settings, not files. They are
not enabled by this repository and cannot be enabled by a commit. If the maintainer
configures them, the `main` ruleset must stay compatible with direct commits:

- require the `Build (macos-latest)`, `Build (windows-latest)`, and `Commit policy`
  checks,
- block force pushes and branch deletion.

A mandatory pull request and a mandatory approving review are deliberately **not**
part of that configuration: either would block the direct-to-`main` workflow, making
this document impossible to follow. Until a ruleset exists, GitHub does not block
direct pushes, and this document describes the required process while CI enforces
what it can.

## Discrepancies with the engine repository

Nymeria has its own `docs/GIT_GUIDELINES.md`, `AGENTS.md`, and CI policy. Two points
are recorded here rather than silently resolved:

1. Both repositories push verified commits directly to `main`. Nymeria additionally
   uses development branches for larger efforts; this repository does not, unless
   the owner explicitly requests one.
2. Nymeria allows Dependabot auto-merge gated on its `Repository checks` workflow.
   This repository intentionally has no dependency bot; engine and package updates
   are deliberate and reviewed.

Neither difference touches engine history or engine policy. Nymeria's own rules
continue to apply inside `external/Nymeria/`; this repository cannot and must not
override them.
