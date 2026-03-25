# jellyfin-plugin-debrid-stream

Jellyfin plugin: **Stremio-style stream addon** + **Real-Debrid** or **TorBox** for library **movies** and **TV episodes** (requires **IMDb** metadata).

## Repository

```bash
git clone https://github.com/killamfkr/jellyfin-plugin-debrid-stream.git
cd jellyfin-plugin-debrid-stream
```

## Requirements

- Jellyfin **10.10+** (built against **Jellyfin.Controller 10.11.6**; change `JellyfinVersion` in the `.csproj` if you need another server build).
- **.NET 9 SDK** to compile.
- Items need an **IMDb** id. Configure **Real-Debrid** and/or **TorBox** in the plugin settings.

## Build

**If `dotnet` is not found in PowerShell** (common in some IDE terminals), either:

- Run **`.\publish.ps1`** from this folder (finds `dotnet.exe` automatically), or  
- Use the full path:  
  `"${env:ProgramFiles}\dotnet\dotnet.exe" publish -c Release -o ./publish`

Add `C:\Program Files\dotnet` to your **user** PATH once: Settings → System → About → Advanced system settings → Environment variables → Path → Edit → New → `C:\Program Files\dotnet` → OK, then **open a new terminal**.

```bash
dotnet publish -c Release -o ./publish
```

Install files from **`publish/`**:

- `Jellyfin.Plugin.DebridStream.dll`
- `meta.json`

## Install in Jellyfin

1. Under Jellyfin’s [plugin directory](https://jellyfin.org/docs/general/administration/configuration/#directory-structure), create a folder (e.g. `DebridStream_1.0.0`).
2. Copy **`Jellyfin.Plugin.DebridStream.dll`** and **`meta.json`** into it.
3. Restart Jellyfin → **Dashboard → Plugins** → configure **Debrid / Stremio streams**.

## GitHub Actions

Pushes and PRs run **`dotnet publish`**. Download the **artifact** from the workflow run for a pre-built DLL + `meta.json`.

## Legal

Use only for content you are allowed to access. Respect Jellyfin, debrid services, and addon providers’ terms.

## License

GPL-2.0-or-later (same family as Jellyfin).
