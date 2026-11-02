using System.Buffers.Binary;
using System.Diagnostics;

namespace CemuPhoneGamePad;

internal sealed record PadState(uint Buttons = 0, float LX = 0, float LY = 0, float RX = 0, float RY = 0,
    bool Touch = false, float TX = 0, float TY = 0, float AX = 0, float AY = -1, float AZ = 0,
    float Pitch = 0, float Yaw = 0, float Roll = 0)
{
    public static PadState Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 80 || !bytes[..4].SequenceEqual("PGPD"u8) || bytes[4] != 1
            || (bytes[5] & ~7) != 0 || bytes[6] != 0 || bytes[7] != 0 || bytes[72..].ContainsAnyExcept((byte)0))
            throw new InvalidDataException("Unsupported phone control packet");
        float[] v = new float[12];
        for (int i = 0; i < v.Length; i++)
        {
            v[i] = BinaryPrimitives.ReadSingleBigEndian(bytes[(16 + 4 * i)..]);
            if (!float.IsFinite(v[i])) throw new InvalidDataException("Invalid control value");
        }
        static float Axis(float x) => Math.Clamp(x, -1, 1);
        bool accel = (bytes[5] & 2) != 0, gyro = (bytes[5] & 4) != 0;
        return new(BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]) & 0x1FFFF,
            Axis(v[0]), Axis(v[1]), Axis(v[2]), Axis(v[3]), (bytes[5] & 1) != 0,
            Math.Clamp(v[4], 0, 1), Math.Clamp(v[5], 0, 1),
            accel ? Math.Clamp(v[6], -8, 8) : 0, accel ? Math.Clamp(v[7], -8, 8) : -1,
            accel ? Math.Clamp(v[8], -8, 8) : 0,
            gyro ? Math.Clamp(v[9], -4000, 4000) : 0, gyro ? Math.Clamp(v[10], -4000, 4000) : 0,
            gyro ? Math.Clamp(v[11], -4000, 4000) : 0);
    }
}

internal sealed class PadSource
{
    private readonly object gate = new();
    private PadState current = new();
    private long received;
    private long packets;
    public void Update(PadState state) { lock (gate) { current = state; received = Environment.TickCount64; packets++; } }
    public (PadState State, bool Connected) Snapshot()
    {
        lock (gate) return received != 0 && Environment.TickCount64 - received < 250 ? (current, true) : (new(), false);
    }
    public string Diagnostic
    {
        get
        {
            lock (gate)
            {
                if (received == 0 || Environment.TickCount64 - received >= 250)
                    return "Phone input: waiting — Controller 1 is neutral";
                string[] names = ["Minus", "L3", "R3", "Plus", "Up", "Right", "Down", "Left", "ZL", "ZR", "L", "R", "X", "A", "B", "Y", "HOME"];
                string pressed = string.Join("+", Enumerable.Range(0, names.Length).Where(i => (current.Buttons & (1u << i)) != 0).Select(i => names[i]));
                if (pressed.Length == 0) pressed = "none";
                return $"Phone input: {packets} packets received · buttons {pressed} · touch {(current.Touch ? "down" : "up")}";
            }
        }
    }
    public void Release() { lock (gate) { current = new(); received = 0; } }
    public static ulong Timestamp => (ulong)(Stopwatch.GetTimestamp() * (1_000_000.0 / Stopwatch.Frequency));
}
