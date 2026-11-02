# Cemu Phone GamePad

Use an **Android phone as Cemu's Wii U GamePad**: a separate GamePad screen,
multitouch buttons and sticks, direct touchscreen input, phone motion, and
PC playback audio. Cemu runs the game on a Windows PC; the TV view stays there.

This is a separate app from Wii U Controller. It installs alongside it as
`com.brian.cemugamepad`. It does not connect to a physical Wii U console.

[Download Android APK and Windows host](https://github.com/TheStonedGamer/cemu-phone-gamepad/releases/latest)

## Requirements

- Windows 10 version 2004+ or Windows 11 x64 PC running Cemu with its **Separate GamePad View** enabled.
- Android 6.0+ phone; gyroscope required for full motion. Buttons and touch work
  without a gyro.
- A shared trusted local network. Prefer 5 GHz Wi-Fi or Ethernet for the PC.
- No virtual controller driver, paid license, or separate .NET runtime required.

## Setup

1. Install `Cemu-Phone-GamePad.apk` on the phone.
2. Run `Cemu-Phone-GamePad-Windows-Setup.exe` on the PC, or extract the full
   portable ZIP and run `Cemu-Phone-GamePad.exe`. The installer adds a Start menu
   shortcut and uninstaller; desktop and sign-in shortcuts are optional.
3. In Cemu, start a game and enable **Options → Separate GamePad View**.
4. In the host, refresh the window list and select **GamePad View**. Click
   **Start host**. The GamePad window can stay behind the main game
   window or other apps; keep it open and not minimized.
5. Allow the host on your private network in Windows Firewall. Open **Menu → Connection** in the phone app and enter the displayed PC IPv4 address and 8-digit code
   once. Pairing is saved automatically on both devices.
6. In the host, click **Install Cemu button mappings…**. Select Cemu's active
   configuration folder (the folder containing `settings.xml`), or its
   `controllerProfiles` folder directly.
7. Reopen Cemu's **Options → Input settings**, choose **Controller 1**, select
   the **CemuPhoneGamePad** profile and click **Load**. This selects Wii U
   GamePad, DSUController at **127.0.0.1:26760**, and maps buttons, sticks and
   motion. Touch is supplied directly by DSU.

### If the image works but buttons do not

- Update and run the entire Windows ZIP, then load the supplied profile above.
  Cemu's DSU backend needs button mappings before it can control a game.
- Watch **Phone input** in the Windows host while pressing A: its packet count
  should advance and it should show A. Waiting means phone input is missing.
- **Cemu input** should show “Controller data requested by the emulator”. If it
  stays “Controller 1 ready”, load the profile or add **DSUController** manually
  at **127.0.0.1**, port **26760**, selecting **Controller 1**.
- Controller 1 is available as soon as the Windows app opens, including before
  pairing or starting video. Disconnected phones produce neutral controls.
- If port 26760 is busy, close the other DSU/motion server and open Diagnostics and click **Retry Cemu input**. Restart Cemu if it cached an older connection.

The host sends DSU data on PC loopback only. The phone connection uses
**TCP 26762**. If DSU 26760 is busy, close another receiver or motion server.
The code changes each time the host starts, but **only new pairings need it**.
Saved phones reconnect with their device token after host/phone restarts and
app updates. On the phone, leave the code blank when selecting a saved PC.

## Everyday use

### Android

- **Menu** (or Android Back) opens Connection, controller/audio settings, help,
  and disconnect/reconnect. Leaving the app releases controls.
- **Audio on / Unmute** is a quick mute button. Settings include volume,
  button haptics, motion, touchscreen contact marker, and stick deadzone.
- **Screen** expands the GamePad image and hides the controller buttons; touch
  still works. **Buttons** restores the complete controller layout.
- The footer shows video fps, outgoing control packet rate and received data
  rate. Audio reports the transport, queued phone playback time and dropped
  packets. These readings do **not** measure total input or audio latency.

### Windows

- **Dashboard** keeps window selection, pairing and live input status together.
  Cemu windows and PC addresses refresh automatically.
- **Settings** remembers playback audio, 15/30 fps, JPEG quality, minimizing to
  the notification area, and optional auto start when GamePad View is found.
  Stream settings take effect when the host is next started.
- Minimizing can keep the host running in the notification area. Double-click
  its icon to reopen it. Closing the app exits and releases controller input.
- **Diagnostics** includes recent activity, retrying the Cemu input connection,
  and copying a report without pairing codes or saved credentials.
- Settings are stored in `%LOCALAPPDATA%\CemuPhoneGamePad\settings.json`.
  The installer and portable build share settings and saved pairings for the
  same Windows user. Uninstalling keeps those preferences.

## Audio delay

Update **both apps to 0.2.0 or newer** to use the separate audio transport.
The default **Low delay · UDP** sends small PCM packets independently of video,
so a delayed JPEG cannot block audio behind it. Windows requests a 20 ms capture
buffer and sends up to 5 ms per packet. Android requests low latency playback
where available, limits its output buffer, and discards stale audio rather than
replaying a growing backlog. Actual device buffer sizes can differ.

In **Menu → Controller & audio settings**, choose:

| Setting | Use |
| --- | --- |
| Fast · 20 ms | Smallest requested playback buffer; try on a strong LAN |
| Balanced · 30 ms | Default compromise for Wi-Fi |
| Stable · 60 ms | More room for jitter if Fast/Balanced crackle |
| Compatibility · TCP | Use if your network blocks UDP audio; video traffic may delay sound |

Use phone speakers or wired headphones when delay matters. Bluetooth and the
PC's playback endpoint can add delay beyond the app buffers. UDP prefers fresh
sound over recovering lost packets, so a weak network can cause gaps. There is
no measured end-to-end latency claim for this release. The host uses an outgoing
UDP socket to a port negotiated by the paired phone; no additional fixed Windows
inbound audio port is required.

### Saved pairings

- Use version **0.1.2 or newer on both PC and phone** for remembered pairings.
  Pair once after upgrading from an older phone build. Older phones can still
  use a new host with its current code, but those sessions are not remembered.
- Android saves a random app identity and a separate credential for each PC
  address in private app preferences. The most recently used PC reconnects when
  the app opens; other saved addresses appear as suggestions in Connection.
- Windows stores device names and token hashes under
  `%LOCALAPPDATA%\CemuPhoneGamePad\pairings.json`. Replacing or moving the host
  ZIP does not remove pairings. Use the same Windows user profile.
- **Saved devices… → Forget selected phone** on Windows revokes that device,
  disconnects an active session, and releases its controls. Pair again using
  the current code if you want to restore access. Up to 16 phones can be saved;
  only one GamePad session runs at a time.
- **Forget this PC** in Android Connection removes the local credential for the
  entered address. Windows' corresponding entry remains until revoked there
  or replaced by a new pairing from the same app installation.
- If the PC's address changes, enter the new address and pair once there.
  Uninstalling the phone app, clearing its data, or deleting Windows' saved
  pairing file also requires pairing again. No hardware serial number is used.

| Phone control | DSU label in Cemu |
| --- | --- |
| A / B / X / Y | Circle / Cross / Triangle / Square |
| L / R / ZL / ZR | L / R / ZL / ZR |
| Plus / Minus | Options / Share |
| L3 / R3 | Stick L / Stick R |
| HOME | Touch (a button separate from touchscreen coordinates) |
| D-pad | Up / Down / Left / Right |
| Left / right sticks | Axis / Rotation directions |

## Current behavior and limits

- The central phone image is touch-sensitive while other fingers hold buttons
  or move sticks. Wii U GamePad touch itself is one contact, as on the original.
- Buttons/sticks/sensors send at about 60 Hz. Input older than 250 ms is replaced
  with neutral controls and released touch. Backgrounding, disconnecting, or
  opening Connection releases controls.
- Video uses JPEG frames at 854×480, targeting 30 fps. Decode and transport queues
  are bounded and discard older frames when the network cannot keep up.
  JPEG encoding prioritizes compatibility over bandwidth efficiency.
- Windows Graphics Capture reads the **selected Cemu window**, even when
  covered by other windows or without keyboard focus. Other desktop windows
  are not sampled. Window decorations and central 16:9 letterboxing are removed
  before the image is sent, keeping touch coordinates aligned.
- Keep GamePad View open and not minimized. Windows or Cemu may suspend frame
  production for minimized windows. If minimized, the phone keeps the last image
  and shows a restore message; controls/audio and the paired session remain
  active. Restoring the window resumes new frames. Resizing recreates the capture
  buffers. If capture fails or the window closes, controls/audio stay connected while
  video retries. To capture a newly opened window, stop, refresh and reselect it.
- Windows Graphics Capture must be available (Windows 10 2004+ with a supported
  GPU/driver). Windows may display its standard capture border. Capture does not
  inject code into Cemu, modify Cemu, or fall back to desktop screenshots.
  HDR-to-SDR tone mapping is not implemented; use SDR for predictable colors.
- Audio is 48 kHz stereo PCM from the PC's default playback endpoint. It includes
  other apps using that endpoint. The host checkbox disables sharing; phone
  settings and the quick button also have mute. It does not isolate Cemu's GamePad-only audio.
- Acceleration and gyro rates are rotated into the phone's landscape display
  basis. Samples older than 200 ms are invalidated. Gyro is optional; orientation
  feel, axis signs and drift require live Cemu checks on real phones.
- One phone/GamePad per host. There is no game streaming to a physical Wii U.
- Camera, microphone, NFC, stylus pressure, rumble, and GamePad-specific power
  controls are not forwarded. This app covers the screen, touch, standard
  controls, motion and playback path rather than every GamePad accessory.
- Pairing uses a one-time code exchange followed by a random per-device token.
  The host stores SHA-256 token hashes; the phone stores the token itself.
  Traffic, including the initial token exchange, is unencrypted. Use a trusted
  local network.

## Validation status

The APK and Windows project compile. Initial installation/launch is checked on
an Android Galaxy A14. A live game image has been observed on that phone. DSU
discovery, packet CRCs, button forwarding and neutral release were checked with
a protocol probe. Actual in-game input and the new audio path on Windows still need confirmation. Covered-window capture,
resize/restore behavior, touch alignment, sensor orientation, audio and actual
latency remain experimental until tested together. Building a package is not
proof of in-game compatibility.

## Build

Android uses a small Java/Canvas client without Android Studio or Gradle:

```sh
docker build -f docker/android.Dockerfile -t gamepad-android .
docker run --rm -v "$PWD:/project" gamepad-android bash build.sh
```

The build generates a local signing key under ignored `android/signing/` if
needed. Keep it for compatible updates. The development key password is
`android`. CI restores its key from `ANDROID_KEYSTORE_BASE64`; pull requests
use a separate temporary development key.

Windows requires the .NET 10 SDK:

```powershell
./host/build.ps1
# Also build the installer (requires Inno Setup 6):
./host/build.ps1 -Installer
```

Outputs are under `dist/`. CI builds both platforms. Pushing a version tag that
matches `VERSION`, such as `v0.2.0`, publishes the APK, portable Windows ZIP,
Windows installer and SHA256SUMS.txt after both builds pass.

See [wire protocol](docs/PROTOCOL.md). Licensed under MIT. Independent software;
not affiliated with Nintendo, Cemu, or Android.

## Capture implementation

The host uses Windows' [HWND capture interop](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow)
and a free-threaded [Windows Graphics Capture frame pool](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture).
This captures Cemu's composed window surface rather than the desktop rectangle.
