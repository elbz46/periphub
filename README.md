# PeriphHub

**[⬇ Download PeriphHub for Windows](https://github.com/elbz46/periphub/releases/latest)** — open the
latest release and run `PeriphHub-Setup-<version>.exe`.

One Windows 10/11 app for all your peripherals — Logitech mice, keyboards and
headsets, ASUS laptop keyboard lighting and performance modes, and any other
Bluetooth/USB mouse, keyboard or headset — instead of Logitech Options+,
G HUB, Armoury Crate and MSI Center.

- **Installer, not a portable app**: `PeriphHub-Setup-<version>.exe` installs to
  `C:\Program Files\PeriphHub`, adds Start-menu entries, an uninstaller
  (Settings › Apps), and optionally starts with Windows in the tray.
- **No setup per device**: devices are detected automatically when you plug
  them in or connect them over Bluetooth. Settings are stored per *physical*
  device (Logitech devices by their unit ID), so moving the MX Master from the
  USB receiver to Bluetooth and back keeps every setting and re-applies it
  instantly — also after sleep, reconnect or reboot.
- **Share it**: friends just run the same setup. Their devices are discovered
  the same way; nothing is hard-coded to one PC. Settings › Export lets you
  hand over your whole configuration.
- No .NET install needed — the runtime is bundled.

> Close Logitech Options+ / G HUB / Armoury Crate while using PeriphHub; they
> overwrite the same device settings.

## What works

### Logitech (HID++ 2.0 — Unifying, Bolt, LIGHTSPEED, Bluetooth, cable)

The device page is built from the features the device itself reports, so
other Logitech models light up automatically.

| Feature | Notes |
| --- | --- |
| Battery level / charging | Battery status, unified battery, voltage and ADC (headsets) |
| DPI | Slider over the exact range the sensor supports; DPI cycle and DPI-shift (sniper) buttons |
| Polling rate, onboard / software mode | G-series mice |
| SmartShift | Ratchet / free-spin / SmartShift with sensitivity |
| Hi-res scrolling, natural scrolling | `0x2121` |
| Thumb wheel | Horizontal scroll (custom speed), volume, zoom, tabs, desktops, Alt-Tab, invert |
| Button remapping | Every divertable button: keystrokes, text, programs/URLs, media, mouse, Windows and browser actions |
| Gestures | Gesture button: click / up / down / left / right, each assignable |
| Per-application profiles | Different button jobs in e.g. `chrome.exe`, `photoshop.exe` |
| Easy-Switch | See / switch the host channel |
| Fn-key inversion | Logitech keyboards |
| Headset sidetone, equalizer + presets | `0x8300`, `0x8310` |
| RGB lighting | Static / breathing / cycle / off (`0x8070`) |

Your devices: **MX Master 2S** (receiver or Bluetooth) is fully covered by the
table above. **G321 LIGHTSPEED** is driven over its USB dongle; whatever it
reports (battery, sidetone, EQ, …) appears automatically. Logitech headsets
do not expose settings over Bluetooth — on Bluetooth you get the battery level
Windows reports.

### ASUS laptops (TUF / ROG / Vivobook) — via the ASUS System Control Interface driver

- Model number shown automatically (read from the BIOS)
- Keyboard RGB: static, breathing, colour cycle, strobing; colour picker; speed
- Backlight brightness (off / low / medium / high)
- Lighting while awake / boot / sleep / shutdown (TUF)
- Operating mode Silent / Performance / Turbo (same as Fn+F5)
- Battery charge limit (40–100 %)
- Panel overdrive, GPU Eco mode (when supported)
- Live fan speeds and temperatures

Per-key-RGB ROG models over USB (Aura HID) are supported for effects and
brightness.

### MSI and other keyboards / mice / headsets

- Detected over USB and Bluetooth, battery level over Bluetooth
- **Key remapping & macros** for *every* keyboard (laptop, MSI, anything):
  click *Add key*, press the key, choose what it does — keystrokes, text
  macros, launch apps/sites, media keys — with per-application profiles.

### Music & sound

- Now playing (title, artist, cover art) for any app that uses Windows media
  controls: Spotify, YouTube in the browser, Media Player, Apple Music, VLC…
- Play/pause, next, previous, seek, shuffle, repeat; pick which app to control
- Volume mixer: default output, master volume, per-app volume and mute
- Headset pages: volume, mute, microphone level/mute, make it the default
  speaker or microphone

### Control deck (on-screen button panel)

A small panel of buttons that stays on top of everything until you minimise
or hide it (default shortcut **Ctrl+Alt+D**, also in the tray menu).

- Any number of rows and columns, button size, see-through level, labels
- Buttons with text, emoji or a picture and a colour; actions include
  keystrokes, text, programs/websites, media, Windows shortcuts, volume, mic
  mute, switching sound output and a soundboard (play a sound file)
- Multi-step buttons: run several actions in a row with pauses
- Live buttons: now playing, volume (scroll to change), microphone, clock,
  device battery
- Pages, optionally switched automatically by the app you are using
- Never takes focus, so shortcuts go to the app you are using; held buttons
  keep keys held (push-to-talk)
- Remembers its position separately for every screen setup and comes back
  onto a visible screen when a monitor is unplugged
- Right-click a button to edit, copy, paste or clear it; edit mode lets you
  drag buttons around
- **Folders**: a button that opens its own sub-page (with a Back button)
- **Timers and stopwatch**: press to start/pause, double-press to reset; the
  timer flashes and plays a sound when it finishes
- **Auto-hide**: park it against a screen edge and it slides away to a thin
  strip, sliding back when the mouse touches it
- **Phone deck**: Control deck › Phone deck › turn it on and scan the QR code.
  Your phone (same Wi-Fi) shows the same buttons; the link carries a secret
  pairing code. The installer allows it through the firewall on private
  networks only.
- **OBS** (OBS 28+, Tools › WebSocket Server Settings): switch scenes,
  record/pause/stream, save replay, virtual camera, mute a source
- **Discord**: mute/deafen by pressing Discord's own keybinds (set the same
  keys in Discord › Settings › Keybinds; Discord does not let other apps
  control voice directly)

### Scenes & automation

One switch for your whole setup: fan mode, laptop keyboard lighting, sound
output and microphone, volume, mouse DPI, deck page, Home Assistant, Spotify,
ShareX, keystrokes… Run a scene by hand, from a deck/phone/mouse button
(“Run scene…”), or automatically when:

- a program starts (and another scene when it closes) – e.g. a game
- a second screen is connected / it's back to the laptop screen only
- the charger is plugged in / the laptop runs on battery
- a headset connects / disconnects, or PeriphHub starts

Starter scenes: Gaming, Work, Night, Away, Docked, Laptop only, On battery,
Plugged in (the automatic ones start switched off).

Headsets: Windows sound and mic move to the headset when it connects and back
when it disconnects; music pauses when it disconnects.

### Batteries

Every battery (laptop, mouse, headset, keyboard) with a 24-hour graph and an
estimate of the time left.

### Connections

- **Home Assistant** (long-lived token, works with a self-signed certificate):
  toggle/on/off entities, call any service, live entity tiles on the deck
  and phone (lights, switches, sensors, climate…)
- **YouTube Music** (installed app / PWA): play/pause/next/previous/seek and
  cover art through Windows media controls; like, dislike, shuffle, repeat,
  volume and search by briefly bringing the YouTube Music window forward and
  pressing its own shortcuts (configurable), then returning to your app
- **Spotify** (your own free developer app, signed in once): like the current
  song, add it to a playlist, play a playlist, move playback to a device
- **ShareX**: region/window/full-screen screenshots, scrolling capture, video
  and GIF recording, stop/pause recording, OCR, colour picker, open folder,
  or any task/workflow
- Deck **stats** tiles: CPU/GPU temperature, fan speeds, CPU and RAM use,
  laptop battery; **snippets** paste saved text instantly

## Honest limits

- **MSI lighting**: MSI has not published the protocol for its peripherals'
  RGB. PeriphHub detects the keyboard and shows its hardware IDs under
  *Diagnostics*; lighting needs one USB capture from MSI Center to add.
- **Logitech Flow** (cursor across computers), **firmware updates** and
  **G-series onboard macro editing** are not implemented.
- Key remapping applies to all keyboards at once (Windows cannot tell which
  keyboard a hooked key came from) and does not affect apps running as
  administrator unless PeriphHub is started as administrator too.
- The installer is not code-signed yet, so Windows SmartScreen shows
  “Windows protected your PC” — click **More info › Run anyway**.

## Helping add MSI keyboard lighting

MSI does not publish how its keyboards' lighting is controlled, so support has
to be learned from one recording of MSI Center talking to the keyboard:

1. Plug the keyboard's USB dongle (or cable) into the PC.
2. Install [Wireshark](https://www.wireshark.org/download.html) and tick
   **Install USBPcap** during setup, then restart the PC.
3. Open Wireshark, double-click the **USBPcap** interface (try each one until
   typing on the keyboard makes lines appear).
4. In MSI Center, set the keyboard to **static red**, wait two seconds, then
   **static green**, wait, then **static blue**.
5. Stop the capture in Wireshark and save it (**File › Save As**, `.pcapng`).
6. Send the file. It only contains USB traffic; still, avoid typing passwords
   while it records.

## Finding your laptop model

PeriphHub shows it on the laptop's page. Without the app: **Settings › System
› About** (“Device name”/“Model”), or run `wmic csproduct get name` in a
terminal, or check the sticker under the laptop.

## Building

Needs the .NET 10 SDK and NSIS.

```bash
./build.sh          # Linux / CI  → out/installer/PeriphHub-Setup-<version>.exe
```

```powershell
./build.ps1         # Windows
```

Every push to `main` builds the installer in GitHub Actions and publishes it as
the release for the `<Version>` in `src/PeriphHub/PeriphHub.csproj`. Bump the
version to publish a new release.

## Layout

```text
src/PeriphHub/
  Core/        settings store, capability model, actions, key hook, hot-plug hub
  Drivers/
    Logitech/  HID++ 2.0 transport, feature discovery, device page
    Asus/      ATKACPI (Armoury Crate interface) + USB Aura
    Generic/   Bluetooth battery, MSI / other vendors
  UI/          tray icon, converters, dialogs
installer/     NSIS script
```

Logs: `%APPDATA%\PeriphHub\periphhub.log` · Settings: `%APPDATA%\PeriphHub\settings.json`
