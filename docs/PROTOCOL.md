# Phone link and emulator output

Phone connects to host TCP 26762. Both peers set TCP_NODELAY. Client first
sends ASCII `PGP1` followed by the host's eight numeric pairing digits, exactly
12 bytes. The host grants one phone session at a time. Pairing has a five-second
deadline; rejected sessions are delayed one second. Traffic is not encrypted.

## Phone to host

Fixed 80-byte records; all multibyte values big endian:

| Offset | Size | Meaning |
| --- | --- | --- |
| 0 | 4 | PGPD |
| 4 | 1 | Version 1 |
| 5 | 1 | Flags: touch 1, accelerometer valid 2, gyro valid 4 |
| 6 | 2 | Zero |
| 8 | 4 | Buttons, DSU state1 low byte/state2 next byte/Home bit 16 |
| 12 | 4 | Sequence (diagnostic; TCP already orders packets) |
| 16 | 16 | Four IEEE float32 stick axes LX/LY/RX/RY; ±1, Y up |
| 32 | 8 | float32 touch X/Y, 0–1, Y down |
| 40 | 12 | float32 DSU acceleration XYZ, g |
| 52 | 12 | float32 DSU gyro pitch/yaw/roll, degrees/s |
| 64 | 8 | Android monotonic sensor clock, microseconds (diagnostic) |
| 72 | 8 | Zero |

Unknown versions/flags/reserved fields and nonfinite numbers terminate the
session. Values are clamped. Phone samples sensors separately; >200 ms old
sensor data loses its validity flag. No fresh input for 250 ms neutralizes
buttons, sticks, motion and touch in the DSU output. Two seconds without input
ends the link. The DSU clock is host monotonic time, independent of phone clock.

## Host to phone

Each frame is one type byte, four-byte big-endian payload length, and payload.
Length is bounded to 2 MB. Types: 1 JPEG video; 2 little-endian signed 16-bit
48 kHz stereo PCM audio; 3 UTF-8 status. Video decode also bounds image size.
Audio/video queue lengths are bounded; older data is discarded under load.
The host sends a type-3 status heartbeat every second even when Windows capture
has no new image, such as a static scene or minimized GamePad View. This keeps
the paired link alive without repeatedly encoding the same frame.

## Cemu

Host exposes DSU/Cemuhook protocol 1001 on 127.0.0.1:26760 only. It implements
version, four-slot information (only slot zero used), subscription by slot/MAC
or all pads, CRC32, packet counters, digital/analog buttons, dual sticks, touch,
and motion. Subscriptions expire after five seconds without renewal.

Touch coordinates map to Cemu's DSU input range 1920×942; touch activation is
independent of the digital Touch button. Home is mirrored to that digital
button because Cemu's DSU backend does not expose the separate PS byte.
