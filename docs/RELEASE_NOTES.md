A new app experience on Android and Windows, with a dedicated audio path to reduce delay.

## Audio

- Low delay UDP audio no longer waits behind JPEG video on TCP. Small PCM packets contain at most 5 ms of sound, with session IDs, sequence checks and freshness timestamps.
- Windows requests a 20 ms capture buffer instead of the previous default 100 ms. Old capture/receive/playback backlog is discarded.
- Android requests low latency playback, uses nonblocking writes, and offers Fast (20 ms), Balanced (30 ms) and Stable (60 ms) playback buffers. These are requested buffer sizes, not measured total latency.
- Live audio transport, queued playback time and drop counts. Compatibility TCP remains available for networks blocking UDP.
- Update both apps for UDP audio. Older builds still connect using TCP audio. Phone speakers or wired headphones avoid additional Bluetooth delay.

## Android

- Cleaner controller interface, colored face buttons, connection indicator, quick mute and screen-only view.
- Menu includes saved PCs, connection, help, disconnect/reconnect and persistent controller/audio preferences.
- Volume, haptics, adjustable stick deadzone, optional motion and touch marker.
- Live fps, input send rate and data rate. Reconnect backoff and session isolation prevent an old connection from interfering with a new one; stopping no longer waits on the UI thread.

## Windows

- Dashboard, Settings, Diagnostics and Help tabs, with guided Cemu profile installation.
- Saved quality/fps/audio preferences, notification-area behavior and optional auto start when GamePad View appears.
- Automatic window/address refresh, copyable pairing details and credential-free diagnostics.
- Video capture errors retry without ending controller input or audio. Stopping/closing runs cleanup without blocking the dashboard.
- New installer with Start menu shortcut, uninstaller and optional desktop/sign-in shortcuts. Portable ZIP remains available. Settings and pairings survive updates.

Both builds compile and Android installation/launch has been checked. Total live Windows-to-phone audio delay has not been measured; complete Windows/Cemu gameplay and audio behavior still need device confirmation. Camera, microphone, NFC and rumble are not forwarded. Playback includes other PC apps. Use a trusted LAN; traffic is not encrypted.
