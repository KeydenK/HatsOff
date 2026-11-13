# Hats Off scenario tests

Run with the Claude Test Kit (dev builds of Hats Off and the Test Kit loaded):

```
/ctk test C:\Users\lordd\Claude\work\HatsOff\tests
/ctk test report
```

That runs every `*.json` in this folder (not the `manual` subfolder). Run one file by giving its path.
The scenario format is described in `ClaudeTestKit\tests\README.md`.

## Common preconditions

- Dev build of Hats Off loaded (the probe only answers in dev builds), Glamourer installed and enabled.
- Hats Off on (`/hatsoff on`).
- Wearing headgear **that is shown** (otherwise there is no `hat HIDDEN` / `hat SHOWN` event to see).
- Standing on foot in the open world (usual spot: Thavnair, zone 957), not in a duty, cutscene or NPC interaction.

## The probe

`HatsOff.TestState` (see `HatsOff/TestProbe.cs`, registered in `Plugin.cs`) answers one line, e.g.:

```
glamourer=available; enabled=on; hidden_by_us=no; reason=none; waiting_for=cutscene+conversation; restore_to=-; refused=no; testing=no; in_dialogue=no; talk_open=no; quest_watch=no; story=on; duty=on; npc=on; quest_anim=off; delay_ms=500; talk_delay_ms=2000; last=Hat restored.
```

- `hidden_by_us`: Hats Off hid the hat and still owes a restore.
- `reason`: why it last decided to hide: `test`, `cutscene`, `conversation`, `quest_animation` or `none`.
- `waiting_for`: `glamourer` (not connected), `end_of_<reason>` (hiding now), `restore_delay` (scene over,
  waiting to give the hat back), `nothing` (off / every option off), or what it watches for (`cutscene+conversation`...).
- `restore_to`: what the hat goes back to afterwards (`shown`, `hidden` if it was already hidden, `-` when idle).
- `refused`: Glamourer refused the change (state locked by something else).
- Options: `story`, `duty`, `npc` (NPC conversations), `quest_anim`, and both restore delays.
- `last`: the last thing it did (same text as in the settings window).

The `reason` is what the last event decided; reading the probe never re-decides anything.

## Tests

| File | Checks | Extra preconditions |
|---|---|---|
| `01-probe.json` | Probe answers, Glamourer connected, Hats Off on, not refused, not hiding anything while idle. | none |
| `02-idle-open-world.json` | 3 s idle + walking and a jump: hat never changes, no `Hat hidden/restored` log lines, no hat FLICKER, no Hats Off warnings. | open world |
| `03-test-command.json` | `/hatsoff test` hides the hat (probe `reason=test`, real `hat HIDDEN`, log line), gives it back after ~15 s (`hat SHOWN`, log line), no FLICKER, no warnings. Takes ~17 s. | hat shown |
| `04-npc-conversation.json` | Talking to an NPC (`/targetnpc`, pad Cross) hides the hat (probe `reason=conversation`, `hat HIDDEN`), it stays hidden while the box is up and for the "keep hidden after a conversation" delay, then comes back (`hat SHOWN`), no FLICKER. | see below |

### 04: NPC conversation preconditions

- The **NPC conversation** option is on in `/hatsoff` (off by default; the test checks `npc=on` and fails early otherwise).
- Standing right next to a **regular NPC with a one-box idle chat** (not a vendor, retainer bell or service NPC:
  Hats Off deliberately leaves those alone), and that NPC is the **nearest** one, since the test targets it with
  `/targetnpc`. To use a fixed NPC instead, replace that step with `{ "cmd": "/target <NPC name>" }`.
- If the NPC says more than one box, add a `{ "wait": 1500 }, { "do": "pad Cross" }` pair per extra box
  before the "close the dialogue" mark. An extra Cross after the chat ends would start a new conversation, so
  don't add more than needed.
- Not mounted, no NPC interaction already running.

## Manual tests (`manual/`)

These need something that can't be triggered on demand, so they're not in the main suite.

- `manual/cutscene.json`: run `/ctk test C:\Users\lordd\Claude\work\HatsOff\tests\manual\cutscene.json`, then
  **start a cutscene by hand within 2 minutes**: in an inn room, the "Unending Journey" book (replay any MSQ cutscene),
  or a quest step that plays one. It checks the probe goes `hidden_by_us=yes; reason=cutscene`, the hat really
  disappears, Hats Off restores it after the cutscene ends (waits up to 7 minutes), and there is no hat FLICKER
  (start, scene changes, end) and no Hats Off warning. Also look at the cutscene copy of your character: it should be
  hatless from its first frame (the Test Kit only sees your real character, so that part is by eye).
  Preconditions: story cutscenes option on (duty cutscenes for one inside a duty), hat shown.

Also by hand only (no reliable trigger): duty cutscenes, quest reactions (bow, kneel... with the quest animation
option), Glamourer unloaded/reloaded mid-cutscene, logging out while the hat is hidden, opening a shop during an
NPC chat (the hat must come back).
