# Project Guidance

## Repository

- Project root: `D:\JuliMvsCalibrationPlcChangeover`.
- Treat `E:\JuliMvs-...\Data\...` paths as field/U-disk sample data, not source paths.
- Source, tests, and generated diagnostics for this workspace should use the project root unless the user explicitly points to U-disk data.

## Required Workflow

- After any requested code change in this repository, run the relevant tests.
- Commit the code change to git after tests pass.
- Push after committing when a git remote is configured.
- Stage only files related to the current task. Do not include unrelated dirty working tree changes from earlier chats.
- If no git remote is configured, create the local commit and report that push is blocked until a remote is added.

## Project Constraints

- Do not change PLC communication logic unless the user explicitly requests it.
- Do not restore defect detection unless the user explicitly requests it.
- Do not implement production Shape-failure retry/fallback as a workaround; the user wants correct template and incoming-part contour extraction at the source.
- Do not loosen Shape thresholds just to reduce NG, because that can output wrong XYR.
- Normal mode must not use ellipse/PCA fallback. Ellipse/PCA semantics are only for the four-way-symmetric mode.
- Do not clear or publish to the U disk unless the user explicitly asks.
- Do not use whole-file `git checkout --` on dirty files; there are intentional changes from prior sessions.

## Test Commands

```powershell
dotnet run --project src\JuliMvs.App.Tests\JuliMvs.App.Tests.csproj -c Release --no-restore
dotnet build JuliMvsCalibrationPlcChangeover.sln -c Release --no-restore
```

If `--no-restore` fails because a test project has no `project.assets.json`, run:

```powershell
dotnet build JuliMvsCalibrationPlcChangeover.sln -c Release
```

## Skill Routing

When the user's request matches an available GStack skill, use it.

- Bugs, unexpected behavior, or troubleshooting: use `/investigate`.
- QA/testing site or app behavior: use `/qa` or `/qa-only`.
- Code review/diff check: use `/review`.
- Save progress: use `/context-save`.
- Resume context: use `/context-restore`.
- Ship, push, deploy, create PR: use `/ship`.

