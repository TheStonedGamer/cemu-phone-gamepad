using System.Security.Cryptography;
using System.Text.Json;

namespace CemuPhoneGamePad;

internal sealed record PairedDevice(string Id, string Name, string TokenHash, DateTimeOffset PairedAt)
{
    public override string ToString() => $"{Name} — paired {PairedAt.LocalDateTime:g}";
}

// Persist under the Windows user profile, not beside the EXE, so replacing or
// moving the portable host does not discard trusted phones. Only hashes of
// credentials are retained on the PC; plaintext tokens live on the phone.
internal sealed class PairingStore
{
    private sealed record SavedFile(int Version, List<PairedDevice> Devices);
    private readonly object gate = new();
    private readonly string path;
    private List<PairedDevice> devices;
    public PairingStore()
    {
        path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CemuPhoneGamePad", "pairings.json");
        if (!File.Exists(path)) { devices = []; return; }
        var saved = JsonSerializer.Deserialize<SavedFile>(File.ReadAllText(path));
        if (saved == null || saved.Version != 1 || saved.Devices == null || saved.Devices.Count > 16
            || saved.Devices.Any(d => d == null || !Guid.TryParseExact(d.Id, "N", out _) || !ValidSecret(d.TokenHash))
            || saved.Devices.Select(d => d.Id).Distinct().Count() != saved.Devices.Count)
            throw new InvalidDataException("Saved pairings could not be read. See %LOCALAPPDATA%\\CemuPhoneGamePad\\pairings.json");
        devices = saved.Devices;
    }
    private static bool ValidSecret(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Convert.FromHexString(secret)));
    public PairedDevice[] Devices { get { lock (gate) return devices.OrderBy(d => d.Name).ToArray(); } }
    public bool Authenticate(string id, string token, out string hash)
    {
        hash = "";
        if (!ValidSecret(token)) return false;
        hash = Hash(token);
        return Authorized(id, hash);
    }
    public bool Authorized(string id, string hash)
    {
        lock (gate)
        {
            var device = devices.FirstOrDefault(d => d.Id == id);
            return device != null && ValidSecret(hash) && CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(device.TokenHash), Convert.FromHexString(hash));
        }
    }
    public (string Token, string Hash) Pair(string id, string name)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid phone identity");
        name = new string(name.Where(c => !char.IsControl(c)).Take(48).ToArray()).Trim();
        if (name.Length == 0) name = "Android phone";
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), hash = Hash(token);
        lock (gate)
        {
            var updated = devices.Where(d => d.Id != id).ToList();
            if (updated.Count >= 16) throw new InvalidOperationException("Saved device limit reached. Forget an old device before pairing another.");
            updated.Add(new(id, name, hash, DateTimeOffset.UtcNow));
            Save(updated); devices = updated;
        }
        return (token, hash);
    }
    public void Forget(string id)
    {
        lock (gate)
        {
            var updated = devices.Where(d => d.Id != id).ToList();
            Save(updated); devices = updated;
        }
    }
    private void Save(List<PairedDevice> updated)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new SavedFile(1, updated), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
