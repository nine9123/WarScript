# WarScript Source Generator

This folder is ignored by Unity (the `~` suffix). It contains the Roslyn
source generator that auto-generates native binding code from `[WsModule]`
and `[WsFunction]` attributes.

## Build

```bash
cd SourceGen~
dotnet build -c Release
cp bin/Release/netstandard2.0/WarScript.SourceGen.dll ../Plugins/
```

## Unity Setup

1. Select `Plugins/WarScript.SourceGen.dll` in the Unity Inspector
2. Disable **Any Platform** under Select platforms
3. Disable **Editor** and **Standalone** under Include Platforms
4. Add Asset Label: **RoslynAnalyzer** (case sensitive, press enter to create)

The DLL only needs rebuilding when you change the generator logic itself.
Adding new `[WsModule]` classes does NOT require rebuilding the DLL.
