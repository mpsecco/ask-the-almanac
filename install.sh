#!/usr/bin/env bash
# Build Ask the Almanac from source and install it into Stardew Valley's Mods folder.
# Works on Linux (Arch, Debian, Ubuntu, Fedora, ...) and macOS. On Windows, use install.ps1.
#
# Usage: ./install.sh [--game-path <path>] [--install-smapi]
#   --game-path <path>  Stardew Valley folder, if it isn't found automatically
#   --install-smapi     Download and install the latest SMAPI if it's missing
set -euo pipefail

cd "$(dirname "$0")"

game_path=""
install_smapi=false
while [[ $# -gt 0 ]]; do
    case "$1" in
        --game-path) game_path="$2"; shift 2 ;;
        --install-smapi) install_smapi=true; shift ;;
        -h|--help) sed -n '2,8p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
done

info() { printf '\033[1;32m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33mWarning:\033[0m %s\n' "$*" >&2; }
fail() { printf '\033[1;31mError:\033[0m %s\n' "$*" >&2; exit 1; }

# Print the command to install a package on this OS.
install_hint() {
    local what="$1"
    if [[ "$(uname)" == "Darwin" ]]; then
        case "$what" in
            dotnet) echo "  brew install --cask dotnet-sdk" ;;
            ollama) echo "  brew install ollama   (or download from https://ollama.com/download)" ;;
        esac
        return
    fi

    local id="" id_like=""
    if [[ -r /etc/os-release ]]; then
        id="$(. /etc/os-release; echo "${ID:-}")"
        id_like="$(. /etc/os-release; echo "${ID_LIKE:-}")"
    fi
    case "$what:$id" in
        dotnet:arch|dotnet:endeavouros|dotnet:manjaro) echo "  sudo pacman -S dotnet-sdk" ;;
        dotnet:fedora) echo "  sudo dnf install dotnet-sdk-8.0" ;;
        dotnet:ubuntu) echo "  sudo apt install dotnet-sdk-8.0" ;;
        dotnet:debian)
            echo "  Debian needs Microsoft's package repository first:"
            echo "  wget https://packages.microsoft.com/config/debian/\$(. /etc/os-release; echo \$VERSION_ID)/packages-microsoft-prod.deb"
            echo "  sudo dpkg -i packages-microsoft-prod.deb && sudo apt update && sudo apt install dotnet-sdk-8.0" ;;
        ollama:*) echo "  curl -fsSL https://ollama.com/install.sh | sh   (Arch: sudo pacman -S ollama, or ollama-cuda / ollama-rocm for GPUs)" ;;
        dotnet:*)
            if [[ "$id_like" == *debian* || "$id_like" == *ubuntu* ]]; then
                echo "  sudo apt install dotnet-sdk-8.0"
            elif [[ "$id_like" == *fedora* || "$id_like" == *rhel* ]]; then
                echo "  sudo dnf install dotnet-sdk-8.0"
            elif [[ "$id_like" == *arch* ]]; then
                echo "  sudo pacman -S dotnet-sdk"
            else
                echo "  See https://learn.microsoft.com/dotnet/core/install/linux"
            fi ;;
    esac
}

# Find the Stardew Valley folder (the one containing "Stardew Valley.dll").
find_game() {
    local candidates=()
    if [[ "$(uname)" == "Darwin" ]]; then
        candidates+=(
            "$HOME/Library/Application Support/Steam/steamapps/common/Stardew Valley/Contents/MacOS"
            "/Applications/Stardew Valley.app/Contents/MacOS"
        )
        local steam_roots=("$HOME/Library/Application Support/Steam")
    else
        local steam_roots=(
            "$HOME/.local/share/Steam"
            "$HOME/.steam/steam"
            "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"
        )
        candidates+=("$HOME/GOG Games/Stardew Valley/game")
    fi

    # every Steam library listed in libraryfolders.vdf, plus the default ones
    local root lib
    for root in "${steam_roots[@]}"; do
        candidates+=("$root/steamapps/common/Stardew Valley")
        if [[ -f "$root/steamapps/libraryfolders.vdf" ]]; then
            while IFS= read -r lib; do
                candidates+=("$lib/steamapps/common/Stardew Valley" "$lib/steamapps/common/Stardew Valley/Contents/MacOS")
            done < <(sed -n 's/.*"path"[[:space:]]*"\(.*\)".*/\1/p' "$root/steamapps/libraryfolders.vdf")
        fi
    done

    local dir
    for dir in "${candidates[@]}"; do
        if [[ -f "$dir/Stardew Valley.dll" ]]; then
            echo "$dir"
            return
        fi
    done
}

# Download the latest SMAPI release and run its installer non-interactively.
install_smapi() {
    local tmp url platform
    tmp="$(mktemp -d)"
    trap 'rm -rf "$tmp"' RETURN
    [[ "$(uname)" == "Darwin" ]] && platform="macOS" || platform="linux"

    url="$(curl -fsSL https://api.github.com/repos/Pathoschild/SMAPI/releases/latest \
        | sed -n 's/.*"browser_download_url": *"\([^"]*-installer\.zip\)".*/\1/p' | head -n1)"
    [[ -n "$url" ]] || fail "Couldn't find the latest SMAPI release. Install it manually from https://smapi.io."

    info "Downloading $(basename "$url")"
    curl -fsSL -o "$tmp/smapi.zip" "$url"
    unzip -q "$tmp/smapi.zip" -d "$tmp"
    local installer
    installer="$(find "$tmp" -path "*/internal/$platform/SMAPI.Installer" -type f | head -n1)"
    [[ -n "$installer" ]] || fail "The SMAPI download didn't contain an installer for $platform."
    chmod +x "$installer"

    info "Installing SMAPI into $game_path"
    "$installer" --install --game-path "$game_path" --no-prompt < /dev/null
}

# 1. .NET SDK
info "Checking for the .NET SDK"
if ! command -v dotnet >/dev/null || ! dotnet --list-sdks 2>/dev/null | grep -qE '^([6-9]|[1-9][0-9])\.'; then
    echo "The .NET SDK (6.0 or newer) is needed to build the mod. Install it with:"
    install_hint dotnet
    echo "Or use Microsoft's installer script: https://learn.microsoft.com/dotnet/core/tools/dotnet-install-script"
    exit 1
fi

# 2. Stardew Valley
info "Looking for Stardew Valley"
[[ -n "$game_path" ]] || game_path="$(find_game)"
[[ -n "$game_path" && -f "$game_path/Stardew Valley.dll" ]] \
    || fail "Couldn't find Stardew Valley. Pass its folder with --game-path \"/path/to/Stardew Valley\"."
echo "    $game_path"

# 3. SMAPI
if [[ ! -f "$game_path/StardewModdingAPI.dll" ]]; then
    if $install_smapi; then
        install_smapi
    else
        fail "SMAPI isn't installed. Re-run with --install-smapi, or install it from https://smapi.io."
    fi
fi

# 4. Build. ModBuildConfig copies the mod into <game>/Mods/AskTheAlmanac and keeps an existing config.json.
info "Building and installing the mod"
dotnet build AskTheAlmanac.csproj -c Release "-p:GamePath=$game_path" -nologo -v quiet
echo "    Installed to $game_path/Mods/AskTheAlmanac"
echo "    Release zip: $(ls bin/Release/net6.0/*.zip 2>/dev/null | tail -n1)"

# 5. Ollama
if command -v ollama >/dev/null; then
    info "Ollama is installed. For the default cloud model, run 'ollama signin' once if you haven't."
else
    warn "Ollama isn't installed. The mod needs it to answer questions. Install it with:"
    install_hint ollama
    echo "  then run 'ollama signin' for cloud models."
fi

info "Done! Launch Stardew Valley through SMAPI, load a save and press F2."
