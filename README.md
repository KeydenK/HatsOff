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

- **Nothing is sent to the game server.** Other players keep seeing your hat, and your `/displayhead` setting is never touched. (Friends synced through Lightless Sync or PlayerSync are the exception, see the FAQ.)
- **Hidden from the start.** Story cutscenes use a copy of your character; Hats Off makes sure that copy appears without the hat, no pop-in.
- **Puts back exactly what you had.** If your hat was visible before the cutscene, it comes back. If it was hidden, it stays hidden.
- **Your choice of cutscenes:** story and quest cutscenes (including Inn replays), duty cutscenes, or both.
- **Optional: while talking to NPCs.** Hide your hat for the whole conversation, even outside of cutscenes, with no flashing between the parts of a conversation (off by default).
  - Vendors, service NPCs and retainers are left alone, so you can see and try on headgear there.
  - Quest objects (glowing spots, items to examine) only count if your character visibly reacts: reaching out, kneeling, a facial expression... A simple pickup keeps your hat on.
- **Optional, experimental: quest animations.** Hide your hat when a quest makes your character react (bow, kneel, hand something over, change expression), even without a cutscene or dialogue box (off by default).
- **Plays nice with other plugins.** If something else shows your hat mid-cutscene, Hats Off hides it again.
- **Featherweight.** Hats Off reacts to game events instead of running every frame. The only exception: during an interaction where a reaction would count, it glances at your character's animation 10 times a second, and stops as soon as the interaction ends.

## Requirements

- [Dalamud](https://goatcorp.github.io/) (XIVLauncher)
- [Glamourer](https://github.com/Ottermandias/Glamourer), which itself needs [Penumbra](https://github.com/xivdev/Penumbra)

Glamourer is what lets Hats Off change your hat **without sending anything to the game server**. If it's missing, the settings window tells you.

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

| Setting | What it does | Default |
| --- | --- | --- |
| Story and quest cutscenes | MSQ, side quests, Inn replays | On |
| Cutscenes inside duties | Dungeons, trials, raids | On |
| Talking to NPCs | Whole conversations outside cutscenes (vendors and retainers excluded, objects only if your character reacts) | Off |
| Quest animations without dialogue (experimental) | Your character reacts to a quest step without any dialogue box | Off |
| Keep hidden after a cutscene | Wait before giving the hat back (covers the fade-out) | 0.5 s |
| Keep hidden after a conversation | Same, for conversations and quest reactions (covers gaps between parts) | 2 s |

| Command | What it does |
| --- | --- |
| `/hatsoff` | Open the settings |
| `/hatsoff on` / `off` / `toggle` | Turn Hats Off on or off |
| `/hatsoff test` | Hide your hat for 15 seconds, to check everything works |

## FAQ

**Can other players see my hat disappear?**
No. Nothing is sent to the game server, so the change only happens on your screen.

**I use Lightless Sync / PlayerSync. Will my synced friends see it?**
A little. These plugins share your Glamourer look with the people you're paired with:
- During cutscenes they pause sharing, so your friends keep seeing your hat. Right as a cutscene ends, they may see it vanish for about a second.
- During NPC conversations, quest objects and quest animations (if you turned those options on) they don't pause, so your friends will see your hat hidden at those moments.

**Why did my hat stay on when I touched a quest object?**
On purpose: objects only hide your hat if your character visibly reacts (reaching out, kneeling, a facial expression...). Picking something up without any reaction keeps your hat on, so it doesn't blink for nothing.

**Why doesn't it hide my hat when I talk to a vendor?**
On purpose: vendor and service NPCs (shops, exchanges, menus) aren't story moments, and you may want to see or try on headgear there. Quest conversations are always included, even with NPCs who also sell things.

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

For conversations, it watches the game's dialogue box and checks who you're talking to: quest conversations count,
vendors and service NPCs (recognised from the game's own data) don't, and objects only count if your character reacts.
A reaction is detected by comparing your character's animations (body, face, forced animations) with how they were when the interaction started,
ignoring the idle animations a character plays on its own.

## Building

Requires the .NET 10 SDK and a local Dalamud install (or set `DALAMUD_HOME`).

```
dotnet build -c Release
```

## License

[MIT](LICENSE) © KeydenK

Thanks to Ottermandias for Glamourer and its API, which made this plugin possible.
