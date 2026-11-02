GamePad View can now stay behind the main Cemu window or other apps.

- Replaced desktop screenshots with Windows Graphics Capture targeting the
  selected Cemu window. Covering windows are not included in the phone image.
- Client-area and 16:9 cropping keep borders and title bars off the touch screen.
- Capture buffers follow window resizing.
- Minimizing shows a restore message without disconnecting controls/audio;
  keep the GamePad window open and not minimized for live video.
- Status heartbeats keep static scenes and temporarily paused capture connected.

Requires Windows 10 version 2004+ or Windows 11 x64 and a supported graphics
driver. No Cemu modification, injected DLL, or virtual controller driver.

Android app and phone protocol remain compatible with the previous host.
Download and extract the entire Windows ZIP to update. The APK is included
with synchronized version metadata.

Compilation is checked; real Windows/Cemu capture, resize and restore behavior
still need live validation. Other limitations remain: no camera, microphone,
NFC or rumble forwarding, and desktop loopback audio includes other apps.
