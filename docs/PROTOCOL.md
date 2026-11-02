# Phone link and emulator output

Phone connects to host TCP 26762. Both peers set TCP_NODELAY. The host grants
one phone session at a time. Authentication has a five-second deadline;
rejected sessions are delayed one second. Traffic is not encrypted.

## Pairing and reconnect

New phone builds send ASCII `PGP2`, a four-byte big-endian JSON length (2–1024),
and UTF-8 JSON. Properties are `deviceId` (32 hex digits, random per app install),
`name` (phone model), and either `code` (host's current eight digits) or `token`
(64 hex digits, 32 random bytes issued on the first successful pairing).
Optional `audioPort` (1024–65535) requests UDP audio at that port on the same
IP address as the authenticated TCP peer. Omit it or use zero for TCP audio.

The host responds using normal host-to-phone framing: type 4 with UTF-8 JSON
`{"saved":true,"token":"..."}` for a new pairing, or `{"saved":true}` when an
existing token was accepted. The phone persists the credential before future
reconnects use it. Type 5 is a UTF-8 rejection reason; the phone stops retrying
that credential and asks the user to open Menu → Connection. An explicitly entered code
replaces the saved credential for that PC after acceptance.

The host stores device ID, display name, SHA-256 token hash and pairing date
under the Windows user's local app data directory. Writes use atomic file
replacement. Forgetting a device removes authorization for its active session
and future connections. Tokens are not returned from the saved-device UI.

Legacy phone builds may send ASCII `PGP1` followed by the host's eight numeric
pairing digits, exactly 12 bytes. These sessions are authenticated by the code
but do not create remembered devices. Both handshake versions then use the
same control and media frames below.

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
48 kHz stereo PCM audio; 3 UTF-8 status; 4 UTF-8 pairing JSON; 5 UTF-8 auth rejection; 6 UTF-8 UDP audio configuration. Video decode also bounds image size.
Audio/video queue lengths are bounded; older data is discarded under load.
The host sends a type-3 status heartbeat every second even when Windows capture
has no new image, such as a static scene or minimized GamePad View. This keeps
the paired link alive without repeatedly encoding the same frame.

## Cemu

Host exposes DSU/Cemuhook protocol 1001 on 127.0.0.1:26760 only. It implements
version, four-slot information (slot zero remains connected while the host app
is open, even without a phone; other slots are disconnected), subscription by slot/MAC
or all pads, CRC32, packet counters, digital/analog buttons, dual sticks, touch,
and motion. Subscriptions expire after five seconds without renewal.

Touch coordinates map to Cemu's DSU input range 1920×942; touch activation is
independent of the digital Touch button. Home is mirrored to that digital
button because Cemu's DSU backend does not expose the separate PS byte.

## UDP audio (0.2.0+)

After authentication and before streaming, a UDP-capable audio session receives
a TCP type-6 JSON frame: `{"transport":"udp","session":"<32 hex digits>","sampleRate":48000,"channels":2}`.
The random 16-byte session ID is unique to this connection. Audio then uses UDP
instead of TCP type 2. Older phone/host combinations keep using TCP type 2.
The phone can explicitly choose compatibility TCP by sending audioPort zero.

Each UDP packet is at most 992 bytes, containing at most 5 ms of signed 16-bit
little-endian stereo PCM at 48 kHz:

| Offset | Size | Meaning |
| --- | --- | --- |
| 0 | 4 | ASCII PGPA |
| 4 | 16 | Session ID, raw bytes |
| 20 | 4 | Big-endian uint32 sequence, wraps |
| 24 | 8 | Big-endian int64 host monotonic milliseconds at send |
| 32 | 4–960 | Stereo PCM, multiple of four bytes |

Only packets from the TCP host IP matching the session ID are accepted. Duplicate
and reordered sequences are dropped; packet loss is not retransmitted. The
receiver estimates clock offset from the minimum observed receive-minus-send
delta and discards packets delayed beyond its selected playback budget. It
also bounds the received queue, expires old queued packets and flushes excess
AudioTrack backlog. Queue statistics exclude hardware, capture and network
latency; they are not an end-to-end latency measurement. Session IDs are not
encryption or protection against an observer on the trusted LAN.

Windows capture requests a 20 ms WASAPI loopback buffer; output chunks contain
at most 240 stereo frames (5 ms). The phone requests low latency AudioTrack
performance on Android 8+ and limits its PCM buffer on Android 7+. Playback uses
nonblocking writes with a freshness deadline. Older compatible devices can use
their platform minimum buffer. Fast/Balanced/Stable request 20/30/60 ms; actual
buffer sizes depend on Android and the output device.
