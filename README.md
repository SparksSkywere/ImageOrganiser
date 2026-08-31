# Image Organiser

Find the same photo across every drive even when the file name, size, or resolution is different.

## Requirements

- Windows 10/11
- For building: [.NET 8 SDK](https://dotnet.microsoft.com/download)

## Install (release packages)

Build portable EXE and installers:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The installer creates **Start Menu** and **Desktop** shortcuts under Skywere Industries (both on by default; you can uncheck them).

### GUI

1. Launch **Image Organiser**
2. **Locations** (Ctrl+L) — entire PC, or specific drives and folders
3. **Scan** (F5) or **Scan entire PC** (F6)
4. Select a group, compare copies, then **Keep this, delete others** or **Delete marked**
5. **Find similar** (Ctrl+F) fingerprints a photo and finds close matches in the last scan

## License
This repository is licensed under the MIT License as provided in the LICENSE file included with the project.
