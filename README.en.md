# Lampac nrs

A collection of modules for **lampac NextGen** by nrsua.

[Українська](README.md) | **English**

## Contents

- [LampaParty - Watch with friends](#lampaparty---watch-with-friends)
- [EpWatch - Episode Watcher](#epwatch---episode-watcher)

---

## LampaParty - Watch with friends

> The module is based on [WatchTogether](https://github.com/lampac-nextgen/lampac/tree/main/Modules/WatchTogether) from the `lampac-nextgen/lampac` project.

A module for **lampac NextGen**. Shared viewing in Lampa: you create a room, share the link with friends - and everyone watches the same stream in sync.

### What it does

- A button in the Lampa header opens the **room browser** with a list of open rooms and a lock icon for password-protected ones.
- Room creation:
  - **from a direct link** (m3u8 / mp4);
  - **from the current stream** (host is already watching something);
  - **from the stream context menu** (`hover:long` on an episode) or **from the player settings** (gear icon) - the **"LampaParty - Watch with friends"** item.
- Player sync over WebSocket: play / pause / seek, soft tempo correction on small drift and hard seek on large drift.
- Notifications about participants' actions with the initiator's name (join, leave, pause, resume, seek).
- **Host handover**: if the host leaves but participants remain in the room - the role automatically passes to the next one, the room keeps living.
- **Episode switching by the host** - all participants automatically move to the new source without reconnecting.
- Password protection (optional), locales `uk` / `en` / `ru`, a room badge with a viewer counter over the player.
- Resilient against idle-timeout proxies (Cloudflare): two-way ping/pong + server heartbeat every 20 s.

### How to connect via `repository.example.yaml`

In `module/repository.example.yaml` add an entry:

```yaml
- repository: https://github.com/nrsua/lampac-nrs
  branch: main
  modules:
    - LampaParty
```

Lampac will automatically download the module and compile it on the fly (`dynamic: true` - no binaries).

#### Settings in Lampa

Plugin URL: `http(s)://<host>:<port>/lampaparty.js`

After restart, the **LampaParty** icon will appear in the Lampa header, and the **"LampaParty - Watch with friends"** item will appear in the episode menu / player settings.

In `Lampa.Settings` under the **LampaParty** section:

- **Username** - empty means `lampac_unic_id`.
- **Use password** - whether to show the password field when creating.
- **Default password** - hint for joining / creation.

### Requirements

- A working **NWS** (`Startup.Nws`) on the lampac side.
- Standard `Shared` and `Core`.

---

## EpWatch - Episode Watcher

A module for **lampac NextGen**. Tracks new episodes of your favorite TV shows and sends Telegram notifications.

### What it does

- Subscription right from the show card in Lampa (the **EpWatch** button).
- Selecting a **specific voice-over** from all online balancers configured in your lampac (UAFlix, Filmix and any custom ones - the module queries them via the standard `/lite/events`).
- Selecting a **specific season** after the voice-over, including upcoming ones (e.g., subscribe to S10 that hasn't been released yet).
- Telegram bot:
  - `/list` - list of subscriptions with progress bars `▓░░░░░░░░░` and statuses 🟢/🔵/🟡/✅/🔴.
  - `/check` - instant check with a "what's new + full status" summary.
  - `/lang` - interface language (`uk` / `en` / `ru`), separate per user.
  - `/unlink` - unlink the account.
- The background checker polls only the needed balancers once an hour (with throttling, a pause until the next episode's TMDB date, a cache).
- A separate **EpWatch** page in the Lampa menu with the list of subscribed shows.
- HTML message formatting: `<blockquote>`, episode poster as preview, expandable description, season progress bars.

### How to connect via `repository.example.yaml`

In `module/repository.example.yaml` add an entry:

```yaml
- repository: https://github.com/nrsua/lampac-nrs
  branch: main
  modules:
    - EpWatch
```

Lampac will automatically download the module, compile it (the module is `dynamic: true` - compiled on the fly, no binaries) and start the TelegramBot + checker.

#### Configuration in `init.conf`

Add the section:

```jsonc
"EpWatch": {
  "enable": true,
  "bot_token": "BOT_TOKEN"
}
```

The minimum required is `bot_token`.

#### Installing the plugin in Lampa

URL: `http(s)://<host>:<port>/epwatch.js`

After restart, the **EpWatch** item will appear in the Lampa menu, and the **EpWatch** button - on show cards.
