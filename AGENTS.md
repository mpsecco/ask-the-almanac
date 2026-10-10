# AGENTS.md

Guidance for AI agents and contributors working on Ask the Almanac, a SMAPI mod for Stardew Valley that adds an in-game AI chat window backed by Ollama and the Stardew wiki. The user-facing docs are in [README.md](README.md).

## Build and test

```bash
dotnet build                    # builds and deploys to <game>/Mods/AskTheAlmanac (Pathoschild.Stardew.ModBuildConfig)
./install.sh                    # what users run: finds the game, checks SMAPI/.NET/Ollama, release build + deploy (install.ps1 on Windows)
dotnet build -c Release         # also writes "bin/Release/net6.0/AskTheAlmanac <version>.zip"

cd tools/PromptHarness          # runs the real prompt, client, wiki tools and session outside the game
dotnet run -- "how much is a coop" "and the big one?" /context
MODEL=glm-5.3-flash:cloud CTX=6000 HIST=300 dotnet run -- ...   # small budgets force pruning and compaction
```

- **Targets:** the mod targets `net6.0`, because the game runs on .NET 6. The harness targets `net10.0` and compiles `OllamaClient.cs`, `WikiTools.cs` and `ChatSession.cs` directly, with its own stand-in `ModConfig`. If you add a setting that those files use, add it to `tools/PromptHarness/Program.cs` as well.
- **Use the harness for every prompt or client change.** Run the standard set: coop, "and the big one?", Abigail gifts plus Monday schedule, best spring crops, Skull Cavern, greenhouse. Check:
  - prices are correct (Coop 4,000g + 300 Wood + 100 Stone; Big Coop 10,000g + 400 Wood + 150 Stone)
  - follow-ups appear
  - answers don't repeat themselves
  - the answer uses the test save's 3,200g correctly
- **UI changes** (`ChatMenu.cs`) can only be checked in-game. Launch Stardew through SMAPI and press F2.
- **Config precedence:** the game's `Mods/AskTheAlmanac/config.json` overrides the defaults in `ModConfig.cs` once it exists. Edit it directly when testing new defaults.

## Code map

| File | Role |
|---|---|
| `ModEntry.cs` | SMAPI entry point. Toggle key, per-tick `session.Update()`, clears on return to title, `almanac_ask` console command. |
| `ModConfig.cs` | `config.json` schema. |
| `ChatSession.cs` | Conversation state, system prompt, slash commands, history compaction, parsing `>>` follow-up lines. |
| `OllamaClient.cs` | `/api/chat` calls, the tool-calling loop, pruning tool results to fit the context, token estimates, error wording. |
| `WikiTools.cs` | `search_wiki` / `read_wiki_page` tools on the MediaWiki API, plus wikitext cleanup. |
| `GameContext.cs` | Game-state summary (date, weather, money, location, skills, luck) for the system prompt. |
| `ChatMenu.cs` | The `IClickableMenu`: frame, message log, scrolling, follow-up buttons, text box. |

## Architecture rules

- **Threading.** Game state and UI may only be touched on the game thread.
  - Network calls run in `Task.Run`.
  - Background code reports back by queuing actions on `ChatSession.pending`, which `Update()` drains on each tick.
  - `ChatSession` lives in `ModEntry`, not the menu, so answers arrive even while the window is closed.
- **Generations.** `Clear()` increments `generation` and cancels the in-flight request. Any background result must check the generation before applying it (see `Post` in `Ask` and the compaction callback).
- **Re-rendering.** `ChatSession.Version` increments on every visible change, and `ChatMenu.update()` re-lays out when it changes. Don't re-wrap text every frame.
- **History vs display.** `history` (what the model sees) is separate from `Lines` (what the player sees).
  - Compaction only touches `history` and `summary`.
  - Failed or empty answers throw `OllamaException` and are never added to history.
- **Context budgets:**
  - `OllamaClient.ContextTokens` is the window in effect: `ContextTokens` (1M) for cloud models, capped at `LocalContextTokens` (32k) for local ones. `num_ctx` is only sent to local models, because Ollama allocates VRAM for the whole window and cloud models ignore it.
  - `HistoryTokens` triggers compaction: the oldest exchanges are summarized, and the last `KeepExchanges` stay word-for-word.
  - `ContextTokens` triggers pruning of old tool results within a turn. Tool results the model hasn't read yet are never pruned.
  - `EstimateTokens` is deliberately high (chars × 0.3), because wikitext tokenizes poorly.
- **UI:**
  - Build from vanilla pieces (`drawTextureBox` with `Game1.menuTexture`, `SpriteText`, `TextBox`, `Game1.mouseCursors` sprites) so the window looks native.
  - All coordinates are UI coordinates (`Game1.uiViewport`).
  - Override `receiveKeyPress` so typing doesn't trigger game hotkeys.

## Gotchas found the hard way

- **Wiki search is AND-matching.** "coop cost" finds nothing. The prompt tells the model to search short topic names, and `SearchAsync` retries word by word on a miss.
- **Don't send `think: false`.** GLM then dumps its reasoning into `content`, ending with a lone `</think>`. Leaving thinking on keeps it in a separate `thinking` field. `GetContent` still strips anything before a `</think>` as a safeguard.
- **Cloud models ignore `num_ctx`**, so it isn't sent to them. Never send a 1M `num_ctx` to a local model, or Ollama will try to allocate it.
- **Models output characters the game font lacks** (emoji, curly quotes, `•`). `ChatMenu.Sanitize` maps or drops them; unknown characters can crash `SpriteFont`.
- **Follow-ups:**
  - Models sometimes omit them. A reminder is added to the outgoing user message only, not stored in history.
  - Models sometimes glue `>>` onto the end of the answer, so `ParseReply` splits on `>>` anywhere.
- **Empty answers.** Models occasionally return an empty answer after tool calls; we retry once with a nudge. This happens most with `glm-5.3-flash` (the default): about 1 in 5 first replies come back empty, and about 1 in 10 still fail after the retry. `glm-5.3:cloud` is the more reliable alternative.
- **Prompt style.** Telling the model "no lists" also suppressed the `>>` lines. The prompt explicitly calls the follow-up lines the one expected line format. Keep the worked example in `SystemPrompt` in sync with any format change.

## Conventions

- Match the surrounding style: file-scoped namespaces, `internal sealed`, XML doc comments on members, comments that explain *why*.
- Player-facing text (errors, `/help`, command output) is in the Almanac's friendly voice.
- Keep `manifest.json` `Version` and the csproj `Version` in sync.
- Keep `install.sh` and `install.ps1` behaving the same (game detection, `--install-smapi`, prerequisite hints), and update INSTALL.md when either changes. `install.ps1` hasn't been run on Windows yet.
