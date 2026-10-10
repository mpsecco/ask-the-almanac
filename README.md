# Ask the Almanac

An in-game chat window for Stardew Valley where you can ask an AI anything about the game, like "how much is a coop?", "what does Abigail love?" or "what should I plant today?".

Answers are checked against the [official Stardew Valley wiki](https://stardewvalleywiki.com) as the AI writes them, so prices, gift tastes and schedules come from the wiki rather than the AI's memory. It also knows your current date, weather, money and location, so the advice fits your save.

- A native-looking popup built from the game's own menu frame, fonts and buttons
- Clickable follow-up questions after every answer
- Remembers the conversation, and summarizes older parts automatically to keep requests small
- Works with **Ollama**, using either free local models or Ollama Cloud models

## Requirements

- Stardew Valley 1.6+
- [SMAPI](https://smapi.io) 4.0+
- [Ollama](https://ollama.com), set up for either:
  - **Ollama Cloud** (recommended): no GPU needed. Requires an ollama.com account, and is subject to its usage limits.
  - **A local model**: free and offline. Needs a GPU with about 10 GB of VRAM for a good model.

## Install

**From source** (any OS): run `./install.sh --install-smapi` on Linux or macOS, or `install.ps1 -InstallSmapi` on Windows. See [INSTALL.md](INSTALL.md) for prerequisites and step-by-step instructions for Windows, macOS, Arch, Debian, Ubuntu and Fedora.

**From a release zip** (no .NET SDK needed):

1. Install SMAPI.
2. Download `AskTheAlmanac-<version>.zip` from [Releases](https://github.com/mpsecco/ask-the-almanac/releases), or run `gh release download --repo mpsecco/ask-the-almanac --pattern '*.zip'`. Unzip it into `Stardew Valley/Mods`.
3. Set up Ollama:
   - **Cloud:** run `ollama signin` once. The default model, `glm-5.3-flash:cloud`, then works through your local Ollama.
   - **Local:** run `ollama pull qwen3:14b`, then set `"Model": "qwen3:14b"` in `config.json` (see below).
4. Launch the game through SMAPI, load a save and press **F2**.

## Using it

Type a question and press **Enter**, or click a suggested follow-up. Scroll with the mouse wheel, PgUp/PgDn or the arrows, and press **Esc** to close. Closing the window while it's thinking is fine: the answer will be waiting when you reopen it.

| Command | What it does |
|---|---|
| `/help` | List commands |
| `/clear` | Forget the conversation and start with a fresh context (also cancels a pending answer) |
| `/model [name]` | Show the current model, or switch to another and save it to the config |
| `/context` | Show how much of the conversation is remembered and the token budgets |

You can also ask from the SMAPI console: `almanac_ask how much is a coop`.

## Configuration

`Mods/AskTheAlmanac/config.json` is created the first time you launch the game with the mod.

| Setting | Default | Description |
|---|---|---|
| `ToggleKey` | `F2` | Key that opens and closes the window. Accepts SMAPI keybinds like `"LeftControl + A"`. |
| `OllamaUrl` | `http://localhost:11434` | Your Ollama server. Use `https://ollama.com` to call Ollama Cloud directly. |
| `Model` | `glm-5.3-flash:cloud` | Model name. Cloud models end in `:cloud`. |
| `ApiKey` | *(empty)* | Only needed when `OllamaUrl` is `https://ollama.com`. Leave the `:cloud` suffix off the model name in that case. |
| `IncludeGameContext` | `true` | Send your date, weather, money, location, skills and luck along with each question. |
| `MaxToolRounds` | `4` | How many rounds of wiki lookups the AI may do per question. |
| `ContextTokens` | `1048576` | Context window for cloud models (GLM 5.3 supports 1M). Wiki results are trimmed if a request would exceed it. |
| `LocalContextTokens` | `32768` | Context window for local models. Ollama reserves memory for all of it, so keep this modest (32k ≈ 5 GB of VRAM for a 14B model). |
| `HistoryTokens` | `32768` | How much conversation is kept word-for-word before older parts are summarized. Capped at half the context window. |

**Tested models:**
- `glm-5.3-flash:cloud` (default): good answers in 4–10s, but about 1 in 10 questions gets no answer (asking again usually works)
- `glm-5.3:cloud`: the most reliable, 3–10s
- `deepseek-v4.1-flash:cloud`: fastest, but occasionally sloppy
- `mistral-large-4:cloud`: accurate but slow
- `qwen3:14b`: local

## Privacy

The following is sent to the Ollama server you configure. With a `:cloud` model, that means Ollama's servers.

- Your questions, and the conversation so far (older parts as a summary)
- If `IncludeGameContext` is on: the in-game date and time, weather, money, farm name, current location, skill levels and today's luck

The mod also fetches pages from stardewvalleywiki.com. Nothing else leaves your computer. Your conversation is kept in memory and is cleared when you return to the title screen or type `/clear`. One exception: summaries of older conversation are currently written to SMAPI's log file, so check the log before sharing it.

## Known limitations

- **F2 is also the default key for [Data Layers](https://www.nexusmods.com/stardewvalley/mods/1691).** If you use both, change `ToggleKey`.
- **Typing needs a keyboard.** Controllers can click the follow-up buttons with the cursor, but can't type questions.
- **Split-screen players share one conversation.**
- **The game keeps running in multiplayer.** As with other menus, time only pauses in single-player.
- **Long questions** are limited to the width of the input box.

## Troubleshooting

- **"I couldn't reach Ollama"**: make sure Ollama is running (`ollama list` should work).
- **"Model isn't available"**: run `ollama pull <model>`, or `ollama signin` for cloud models.
- **The window doesn't open**: another mod may use the same key. Change `ToggleKey`.
- **A wrong answer**: AI can still make mistakes. Ask it to double-check, or try a different model with `/model`.

## Building from source

Requires the .NET SDK (6.0 or newer). The install scripts are the easiest route (see [INSTALL.md](INSTALL.md)). Running `dotnet build` directly builds the mod and copies it into your game's `Mods` folder. `dotnet build -c Release` also creates a release zip in `bin/Release/net6.0`. See [AGENTS.md](AGENTS.md) for how the code is organized and how to test prompt changes.

## Credits

Game facts come from the [Stardew Valley Wiki](https://stardewvalleywiki.com), whose content is licensed under [CC BY-NC-SA 3.0](https://creativecommons.org/licenses/by-nc-sa/3.0/). Stardew Valley is by ConcernedApe. This mod isn't affiliated with either.
