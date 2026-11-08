<p align="center">
  <img src="HatsOff/images/icon.png" width="128" alt="Hats Off icon">
</p>

<h1 align="center">Hats Off</h1>

<p align="center">
  A Dalamud plugin for FINAL FANTASY XIV that <b>automatically hides your headgear during cutscenes</b>,<br>
  and puts it back the moment they end.
</p>

---

## Why?

Your helmet looks great in combat. It looks less great covering your face during every emotional story moment.
The game has no option for this, so Hats Off does it for you.

## Features

- **Only you see it.** Nothing is sent to the server: other players keep seeing your hat, and your `/displayhead` setting is never touched.
- **Hidden from the start.** Story cutscenes use a copy of your character; Hats Off makes sure that copy appears without the hat, no pop-in.
- **Puts back exactly what you had.** If your hat was visible before the cutscene, it comes back. If it was hidden, it stays hidden.
- **Your choice of cutscenes:** story and quest cutscenes (including Inn replays), duty cutscenes, or both.
- **Optional: while talking to NPCs.** Hide your hat for the whole conversation, even outside of cutscenes (off by default). Retainers and shops are left alone.
- **Plays nice with other plugins.** If something else shows your hat mid-cutscene, Hats Off hides it again.
- **Shops stay normal.** With the NPC option on, opening a shop gives your hat back, so you can see (and try on) headgear.
- **Featherweight.** Nothing runs every frame: Hats Off only reacts when the game tells it something changed.

## Requirements

- [Dalamud](https://goatcorp.github.io/) (XIVLauncher)
- [Glamourer](https://github.com/Ottermandias/Glamourer), which itself needs [Penumbra](https://github.com/xivdev/Penumbra)

Glamourer is what lets Hats Off change your hat **only on your screen**. If it's missing, the settings window tells you.

## Installation

1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste this URL in an empty line and click the **+** button:
   ```
   https://raw.githubusercontent.com/KeydenK/HatsOff/main/repo.json
   ```
3. Click **Save and Close**.
4. Type `/xlplugins`, search for **Hats Off** and click **Install**.

That's it: it works right away, no setup needed.

## Usage

Type `/hatsoff` to open the settings window.

| Command | What it does |
| --- | --- |
| `/hatsoff` | Open the settings |
| `/hatsoff on` / `off` / `toggle` | Turn Hats Off on or off |
| `/hatsoff test` | Hide your hat for 15 seconds, to check everything works |

## FAQ

**Can other players see my hat disappear?**
No. The change only happens on your screen.

**Does it change my `/displayhead` setting?**
No. Your in-game setting is never modified.

**After a cutscene, Glamourer shows my hat setting as changed. Is that normal?**
Yes. Hats Off goes through Glamourer, so Glamourer remembers that the hat setting was touched. It's harmless, and your hat looks exactly like before.

**I use AutoVisor (or a similar plugin). Is it compatible?**
Yes. If another plugin shows your hat during a cutscene, Hats Off hides it again, and lets that plugin's choice win once the cutscene is over.

**Something doesn't work.**
Open `/hatsoff`, expand **Advanced**, and include the *Last action* line when you [open an issue](https://github.com/KeydenK/HatsOff/issues).

## How it works (for the curious)

Hats Off listens for the game's cutscene state. When a cutscene starts, it asks Glamourer to hide the hat on your character.
Glamourer treats the cutscene copy of your character as *you*, so the copy is created without the hat.
When the cutscene ends (after a short, configurable delay to cover the fade-out), the previous hat state is restored.

## Building

Requires the .NET 10 SDK and a local Dalamud install (or set `DALAMUD_HOME`).

```
dotnet build -c Release
```

## License

[MIT](LICENSE) © KeydenK

Thanks to Ottermandias for Glamourer and its API, which made this plugin possible.
