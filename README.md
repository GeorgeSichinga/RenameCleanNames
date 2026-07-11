# RenameCleanNames

A small Windows Explorer right-click tool that cleans up filenames and audio
metadata that have been polluted with tracker/watermark text from download
sources — things like `Song (somesite.com).mp3` or an ID3 Title field that
reads `Song - somesite.com`.

Right-click any folder → **"Rename with Clean Names"** → review a full
preview of every change → confirm → done.

## What it does

- Recursively walks a folder and strips any `(...)` or `[...]` tag from
  every file and subfolder name.
- Strips bare domain-like text too (e.g. `www.example.com`), even when it
  isn't wrapped in brackets — this is common in ID3 title tags.
- For supported audio files (`.mp3`, `.m4a`, `.flac`, `.ogg`, `.wav`,
  `.wma`), also cleans the Title, Artist, and Album metadata fields using
  the same rules, via [TagLibSharp](https://github.com/mono/taglib-sharp).
- Shows a full preview of every planned change — filenames and metadata —
  and asks for confirmation before touching anything on disk.
- Renames deepest items first, so a folder rename never breaks a path that
  was already computed for something inside it.

## What it doesn't do

- It won't fix a watermark burned into embedded cover art — that's part of
  the image itself, not a text field, and needs different tooling (or
  replacement artwork) entirely.
- It only recognises the audio extensions listed above. Other file types
  get filename cleanup only, no metadata pass.

## Build

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```
dotnet publish -c Release
```

Produces a single self-contained `RenameCleanNames.exe` at
`bin/Release/net8.0/win-x64/publish/`.

## Install (Explorer right-click integration)

1. Copy `RenameCleanNames.exe` to a permanent location, e.g.
   `C:\Tools\RenameCleanNames\RenameCleanNames.exe`.
2. If you used a different path, edit `install.reg` to match.
3. Right-click `install.reg` → **Merge** (or `reg import install.reg` from
   an elevated PowerShell).
4. Right-click any folder in Explorer → **"Rename with Clean Names"**.

## Uninstall

```
reg delete "HKEY_CLASSES_ROOT\Directory\shell\RenameWithCleanNames" /f
```

## Example

```
Before:  Song (www.example.com).mp3   [Title: "Song - www.example.com"]
After:   Song.mp3                      [Title: "Song"]
```

## Design notes

- Registered as a plain `.exe` invoked via a registry command, not a COM
  `IShellFolder` shell extension — much simpler to build, register, and
  debug for a single context-menu action.
- The registry key lives under `Directory\shell` (not `Folder\shell`),
  which is what Explorer actually consults for a normal folder's
  right-click menu.
- Metadata is edited before the file is renamed, so file paths stay valid
  throughout the operation.

## License

MIT — see [LICENSE](LICENSE).
