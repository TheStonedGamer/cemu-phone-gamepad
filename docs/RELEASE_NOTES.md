Fix Cemu controller discovery and provide ready-made GamePad mappings.

- Controller 1 is available immediately when the Windows app opens, before phone pairing or video capture. Stale/disconnected phone input stays neutral.
- Install Cemu button mappings from the host, then select CemuPhoneGamePad and Load in Cemu Input settings for Controller 1. Choose Cemu’s configuration folder containing settings.xml when installing.
- Separate phone input and Cemu request indicators show whether phone packets arrive and whether the emulator requests controller data.
- Recover from closed emulator UDP endpoints without stopping the controller service. A retry button handles port conflicts.
- Existing saved pairings and background GamePad window capture are retained. Android version updated to match; no re-pairing required.

Use DSUController at 127.0.0.1:26760, Controller 1. No virtual controller driver is required. Extract the whole Windows ZIP before running.

DSU protocol discovery, CRCs, A-button forwarding and neutral release have been checked with a protocol probe. Live Windows/Cemu in-game input still needs confirmation. Keep GamePad View open and not minimized. Other limits remain: no camera, microphone, NFC or rumble; playback audio includes other apps; LAN traffic is unencrypted.
