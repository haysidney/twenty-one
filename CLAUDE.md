## Project

This is a Final Fantasy XIV plugin that uses the Dalamud API (https://dalamud.dev/api/).

It's a plugin meant for a dealer to use to run a blackjack game in a venue.

## Build

Build commands:
```bash
dotnet build TwentyOne/TwentyOne.csproj -c Debug
dotnet build TwentyOne/TwentyOne.csproj -c Release
```

## Design Decisions

## Testing

Use only these player names in test cases: Lorah, Bekki, Nolla. When a test requires a winning player, that player must always be Lorah. Write tests for all new features.

## State Persistence

All game state must survive plugin restarts. When adding new UI state or settings, always save them in `GameState` (Configuration.cs), call `SaveState()` at any point the value changes, and restore them in `LoadState()` (MainWindow.cs).
