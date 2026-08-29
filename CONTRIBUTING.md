# Contributing

Bug reports, focused fixes and workflow-oriented suggestions are welcome.

## Before contributing

- Search existing issues before opening a new one.
- Open an issue before a major architectural or UX change.
- Keep changes focused on TraceDeck FE's manual reference-overlay and color-assistance goals.
- Do not add game capture, injection, memory access, automatic tracing or unrelated telemetry.
- Do not attach private `.TDFE` projects or reference images unless you have reviewed and intentionally made them public.

## Build and test

Use Windows 10/11 x64 with the .NET 8 SDK:

```powershell
dotnet restore TraceDeckFE.sln
dotnet build TraceDeckFE.sln -c Debug --no-restore
dotnet test TraceDeckFE.sln -c Debug --no-build
dotnet build TraceDeckFE.sln -c Release --no-restore
dotnet test TraceDeckFE.sln -c Release --no-build
```

Submit changes only after the relevant tests pass with no new warnings. Update documentation when behavior or public contracts change.

