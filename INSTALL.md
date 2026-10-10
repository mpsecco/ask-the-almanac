# Installing on another machine

The install scripts build the mod from source and copy it into the game's `Mods` folder. They find the game automatically, including extra Steam libraries, Flatpak Steam, GOG and the Xbox app. They can also install SMAPI for you. Your existing `config.json` is kept when you reinstall.

## 1. Get the code

The repository is private, so sign in with the GitHub CLI first:

```bash
gh auth login
gh repo clone mpsecco/ask-the-almanac
cd ask-the-almanac
```

## 2. Install the prerequisites

You need the **.NET SDK** (6.0 or newer) to build, and **Ollama** to answer questions. Stardew Valley must already be installed. SMAPI can be installed by the script.

| OS | .NET SDK | Ollama |
|---|---|---|
| Windows | `winget install Microsoft.DotNet.SDK.8` | `winget install Ollama.Ollama` |
| macOS | `brew install --cask dotnet-sdk` | `brew install ollama` or [ollama.com/download](https://ollama.com/download) |
| Arch | `sudo pacman -S dotnet-sdk` | `sudo pacman -S ollama` (or `ollama-cuda` / `ollama-rocm` to run models on your GPU) |
| Ubuntu | `sudo apt install dotnet-sdk-8.0` | `curl -fsSL https://ollama.com/install.sh \| sh` |
| Debian | Add [Microsoft's package repo](https://learn.microsoft.com/dotnet/core/install/linux-debian), then `sudo apt install dotnet-sdk-8.0` | `curl -fsSL https://ollama.com/install.sh \| sh` |
| Fedora | `sudo dnf install dotnet-sdk-8.0` | `curl -fsSL https://ollama.com/install.sh \| sh` |

Any newer SDK works too, such as `dotnet-sdk-10.0` where your distro packages it. If your distro has no package, use Microsoft's [dotnet-install script](https://learn.microsoft.com/dotnet/core/tools/dotnet-install-script).

Then sign in to Ollama once, so the default cloud model works:

```bash
ollama signin
```

## 3. Run the installer

**Linux and macOS:**

```bash
./install.sh --install-smapi
```

**Windows** (PowerShell):

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1 -InstallSmapi
```

`--install-smapi` / `-InstallSmapi` only does something if SMAPI is missing. If the script can't find the game, pass its folder:

```bash
./install.sh --game-path "/path/to/Stardew Valley"
```
```powershell
.\install.ps1 -GamePath "D:\SteamLibrary\steamapps\common\Stardew Valley"
```

## 4. Launch the game with SMAPI

- **Linux and macOS:** launch from Steam as usual. The SMAPI installer replaces the game's launcher.
- **Windows:** in Steam, right-click Stardew Valley → **Properties** → **Launch Options**, and enter:
  ```
  "C:\path\to\Stardew Valley\StardewModdingAPI.exe" %command%
  ```
  Use your game folder; the installer prints the exact line. For GOG or the Xbox app, start `StardewModdingAPI.exe` directly.

Load a save and press **F2**.

## Updating

```bash
git pull
./install.sh            # or install.ps1 on Windows
```

## Without building: copy the zip

Every install also writes `bin/Release/net6.0/AskTheAlmanac <version>.zip`. On a machine that already has SMAPI, you can skip the .NET SDK: unzip that file into `Stardew Valley/Mods`, so you end up with `Mods/AskTheAlmanac/AskTheAlmanac.dll`.

## Where things are

| | Path |
|---|---|
| Mod folder | `<game folder>/Mods/AskTheAlmanac` |
| Settings | `<game folder>/Mods/AskTheAlmanac/config.json` (created on first launch) |
| SMAPI log | Windows `%AppData%\StardewValley\ErrorLogs\SMAPI-latest.txt`; Linux and macOS `~/.config/StardewValley/ErrorLogs/SMAPI-latest.txt` |
| Default Steam game folder | Windows `C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley`; Linux `~/.local/share/Steam/steamapps/common/Stardew Valley`; macOS `~/Library/Application Support/Steam/steamapps/common/Stardew Valley/Contents/MacOS` |
