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
2. Extract the full `Cemu-Phone-GamePad-Windows-x64.zip` on the PC and run
   `Cemu-Phone-GamePad.exe`.
3. In Cemu, start a game and enable **Options → Separate GamePad View**.
4. In the host, refresh the window list and select **GamePad View**. Click
   **Start GamePad host**. The GamePad window can stay behind the main game
   window or other apps; keep it open and not minimized.
5. Allow the host on your private network in Windows Firewall. Open **SETUP**
   in the phone app and enter the displayed PC IPv4 address and 8-digit code
   once. Pairing is saved automatically on both devices.
6. In Cemu's **Options → Input settings**, choose **Wii U GamePad** as the
   emulated controller. Add **DSUController**, server **127.0.0.1**, port
   **26760**, **Controller 1**. Keep the phone connected while configuring.
7. In that controller's **Settings**, enable motion. Map the buttons and sticks
   using the phone controls, then save a named Cemu profile. Touch is provided
   directly by the DSU controller; there is no mouse mapping step.

The host sends DSU data on PC loopback only. The phone connection uses
**TCP 26762**. If DSU 26760 is busy, close another receiver or motion server.
The code changes each time the host starts, but **only new pairings need it**.
Saved phones reconnect with their device token after host/phone restarts and
app updates. On the phone, leave the code blank when selecting a saved PC.

### Saved pairings

- Use version **0.1.2 or newer on both PC and phone** for remembered pairings.
  Pair once after upgrading from an older phone build. Older phones can still
  use a new host with its current code, but those sessions are not remembered.
- Android saves a random app identity and a separate credential for each PC
  address in private app preferences. The most recently used PC reconnects when
  the app opens; other saved addresses appear as suggestions in SETUP.
- Windows stores device names and token hashes under
  `%LOCALAPPDATA%\CemuPhoneGamePad\pairings.json`. Replacing or moving the host
  ZIP does not remove pairings. Use the same Windows user profile.
- **Saved devices… → Forget selected phone** on Windows revokes that device,
  disconnects an active session, and releases its controls. Pair again using
  the current code if you want to restore access. Up to 16 phones can be saved;
  only one GamePad session runs at a time.
- **Forget this PC** in Android SETUP removes the local credential for the
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
  opening SETUP releases controls.
- Video uses JPEG frames at 854×480, targeting 30 fps. Decode and transport queues
  are bounded and discard older frames when the network cannot keep up.
  This first version prioritizes compatibility over bandwidth efficiency.
- Windows Graphics Capture reads the **selected Cemu window**, even when
  covered by other windows or without keyboard focus. Other desktop windows
  are not sampled. Window decorations and central 16:9 letterboxing are removed
  before the image is sent, keeping touch coordinates aligned.
- Keep GamePad View open and not minimized. Windows or Cemu may suspend frame
  production for minimized windows. If minimized, the phone keeps the last image
  and shows a restore message; controls/audio and the paired session remain
  active. Restoring the window resumes new frames. Resizing recreates the capture
  buffers. Closing the window ends the session; reopen and reselect it.
- Windows Graphics Capture must be available (Windows 10 2004+ with a supported
  GPU/driver). Windows may display its standard capture border. Capture does not
  inject code into Cemu, modify Cemu, or fall back to desktop screenshots.
  HDR-to-SDR tone mapping is not implemented; use SDR for predictable colors.
- Audio is 48 kHz stereo PCM from the PC's default playback endpoint. It includes
  other apps using that endpoint. The host checkbox disables sharing; phone
  SETUP also has mute. It does not isolate Cemu's GamePad-only audio.
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
an Android Galaxy A14. Remembered-pairing and capture changes compile, but a live
Windows/Cemu game session has **not** been checked. Covered-window capture,
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
```

Outputs are under `dist/`. CI builds both platforms. Pushing a version tag that
matches `VERSION`, such as `v0.1.2`, publishes the APK, portable Windows ZIP,
and SHA256SUMS.txt after both builds pass.

See [wire protocol](docs/PROTOCOL.md). Licensed under MIT. Independent software;
not affiliated with Nintendo, Cemu, or Android.

## Capture implementation

The host uses Windows' [HWND capture interop](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow)
and a free-threaded [Windows Graphics Capture frame pool](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture).
This captures Cemu's composed window surface rather than the desktop rectangle.
