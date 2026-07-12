# Build & Run

```powershell
# Build
dotnet build

# Run
dotnet run --project src\EVO.ModManager.App

# Run with verbose logging
$env:SERILOG_MINIMUM_LEVEL="Debug"; dotnet run --project src\EVO.ModManager.App

# Tests
dotnet test

# Clean
dotnet clean

# Publish (single-file, trimmed)
dotnet publish src\EVO.ModManager.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/
```

# Project Structure
- `src/EVO.ModManager.App` — WPF desktop app (Views, ViewModels, Styles, Converters)
- `src/EVO.ModManager.Core` — Business logic (Services, Models, Data)
- `tests/EVO.ModManager.Tests` — Unit tests

# Key Files
- `app.config` is not used — all config in `%LOCALAPPDATA%\EVO Mod Manager\`
- Database at `%LOCALAPPDATA%\EVO Mod Manager\evomm.db`
- Logs at `%LOCALAPPDATA%\EVO Mod Manager\logs\evomm-*.log`
