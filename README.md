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
  - **Ollama Cloud** (recommended): no GPU needed. Requires a free ollama.com account.
  - **A local model**: free and offline. Needs a GPU with about 10 GB of VRAM for a good model.

## Install

**From source** (any OS): run `./install.sh --install-smapi` on Linux or macOS, or `install.ps1 -InstallSmapi` on Windows. See [INSTALL.md](INSTALL.md) for prerequisites and step-by-step instructions for Windows, macOS, Arch, Debian, Ubuntu and Fedora.

**From a release zip:**

1. Install SMAPI.
2. Unzip this mod into `Stardew Valley/Mods`.
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
- `glm-5.3-flash:cloud` (default): good answers, 4–10s, occasionally returns no answer (try again)
- `glm-5.3:cloud`: the most reliable, 3–10s
- `deepseek-v4.1-flash:cloud`: fastest, but occasionally sloppy
- `mistral-large-4:cloud`: accurate but slow
- `qwen3:14b`: local

## Privacy

Your questions, and a short summary of your save (if `IncludeGameContext` is on), are sent to the Ollama server you configure. With a `:cloud` model, that means Ollama's servers. The mod also fetches pages from stardewvalleywiki.com. Nothing else leaves your computer.

## Troubleshooting

- **"I couldn't reach Ollama"**: make sure Ollama is running (`ollama list` should work).
- **"Model isn't available"**: run `ollama pull <model>`, or `ollama signin` for cloud models.
- **The window doesn't open**: another mod may use the same key. Change `ToggleKey`.
- **A wrong answer**: AI can still make mistakes. Ask it to double-check, or try a different model with `/model`.

## Building from source

Requires the .NET SDK (6.0 or newer). The install scripts are the easiest route (see [INSTALL.md](INSTALL.md)). Running `dotnet build` directly builds the mod and copies it into your game's `Mods` folder. `dotnet build -c Release` also creates a release zip in `bin/Release/net6.0`. See [AGENTS.md](AGENTS.md) for how the code is organized and how to test prompt changes.

## Credits

Game facts come from the [Stardew Valley Wiki](https://stardewvalleywiki.com), whose content is licensed under [CC BY-NC-SA 3.0](https://creativecommons.org/licenses/by-nc-sa/3.0/). Stardew Valley is by ConcernedApe. This mod isn't affiliated with either.
