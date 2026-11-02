Remembered device pairings, plus GamePad capture behind other windows.

- Pair once; saved phones reconnect without entering a new code after app or
  Windows host restarts. Pairings survive normal app/host updates.
- Android saves separate PC credentials and suggests saved addresses in SETUP.
- Windows Saved devices lists trusted phones and can forget/revoke a selected
  phone, including disconnecting its active session.
- Host stores token hashes under the Windows user profile; the phone stores
  its random credential privately. No hardware device serial is used.
- Includes v0.1.1 Windows Graphics Capture: GamePad View can remain behind other
  windows. Keep it open and not minimized for live video; minimization shows a
  restore message while keeping controls/audio and the connection alive.

Update BOTH the Android APK and the entire Windows ZIP to enable saved
pairings. Enter the host's current code once, then leave the code blank for
that saved PC. Older phones still work with a current code but cannot save
pairings on the host.

Windows 10 version 2004+ or Windows 11 x64 with a supported graphics driver.
No virtual gamepad driver or paid license. Use a trusted LAN; traffic and the
initial pairing exchange are not encrypted.

Builds and Android launch are checked. Complete live Windows/Cemu capture and
pair/reconnect/revoke behavior still need device validation. Other limitations
remain: no camera, microphone, NFC or rumble forwarding, and desktop loopback
audio includes other apps.
