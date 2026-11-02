Initial experimental Android phone GamePad for Cemu on Windows.

- GamePad second-screen video, touch, buttons, dual sticks and phone motion.
- Optional PC playback audio on the phone.
- Paired local connection and neutral release after stale input.
- Direct DSU input to Cemu; no virtual gamepad driver or paid driver license.

Install the APK on Android and extract the entire Windows ZIP on the PC.
Enable Cemu's Separate GamePad View, select it in the host, start the host,
and pair the phone with the displayed address/code. In Cemu select Wii U
GamePad with DSUController at 127.0.0.1:26760, enable motion, and map buttons.
See README.md for setup and limitations.

Keep GamePad View visible and unobscured: this version captures its visible
desktop rectangle. Audio captures all playback on the default PC endpoint.
No camera, microphone, NFC or rumble forwarding.

Android installation/launch and compilation are checked. A complete live
Windows/Cemu session is still unverified; motion orientation, streaming,
touch alignment and latency remain experimental.
