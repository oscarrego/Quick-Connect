using System.IO;
using System.Text.Json;

namespace QuickConnect
{
    // ════════════════════════════════════════════════════════════════════════════
    //  DeviceType — categories we can assign to a Bluetooth device
    // ════════════════════════════════════════════════════════════════════════════
    public enum DeviceType
    {
        Unknown,
        Earbuds,
        Headphones,
        Phone,
        Computer,
        Speaker,
        Keyboard,
        Mouse,
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  DeviceTypeService
    //  Detects device type from Class of Device bits and persists user overrides.
    // ════════════════════════════════════════════════════════════════════════════
    internal static class DeviceTypeService
    {
        private static readonly string SettingsPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuickConnect", "device-types.json");

        // address string → DeviceType string
        private static Dictionary<string, string> _overrides = [];

        public static void Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                string json = File.ReadAllText(SettingsPath);
                _overrides = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                             ?? [];
            }
            catch { _overrides = []; }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath,
                    JsonSerializer.Serialize(_overrides,
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        /// <summary>Returns user-set override if any, otherwise auto-detects from CoD.</summary>
        public static DeviceType GetType(string addressString, uint classOfDevice)
        {
            if (_overrides.TryGetValue(addressString, out string? saved)
                && Enum.TryParse<DeviceType>(saved, out var saved2))
                return saved2;

            return DetectFromCoD(classOfDevice);
        }

        public static void SetOverride(string addressString, DeviceType type)
        {
            _overrides[addressString] = type.ToString();
            Save();
        }

        public static void RemoveOverride(string addressString)
        {
            _overrides.Remove(addressString);
            Save();
        }

        // ── Class of Device bit-field detection ────────────────────────────── //
        // Bluetooth CoD format:  bits[12:8] = Major Device Class
        //                        bits[7:2]  = Minor Device Class
        private static DeviceType DetectFromCoD(uint cod)
        {
            uint major = (cod >> 8) & 0x1F;
            uint minor = (cod >> 2) & 0x3F;

            return major switch
            {
                0x01 => DeviceType.Computer,
                0x02 => DeviceType.Phone,
                0x04 => minor switch          // Audio / Video
                {
                    0x01 => DeviceType.Earbuds,     // Wearable headset
                    0x02 => DeviceType.Headphones,  // Hands-free
                    0x04 => DeviceType.Headphones,  // Microphone (treat as headphones)
                    0x05 => DeviceType.Speaker,     // Loudspeaker
                    0x06 => DeviceType.Headphones,  // Headphones
                    0x07 => DeviceType.Headphones,  // Portable audio
                    _    => DeviceType.Headphones,
                },
                0x05 => minor switch          // Peripheral
                {
                    0x01 => DeviceType.Keyboard,
                    0x02 => DeviceType.Mouse,
                    _    => DeviceType.Unknown,
                },
                _ => DeviceType.Unknown,
            };
        }

        // ── Icon glyph lookup (Segoe MDL2 Assets) ────────────────────────── //
        public static string GetGlyph(DeviceType type) => type switch
        {
            DeviceType.Earbuds     => "\uEBDA",   // Headset / earbuds silhouette
            DeviceType.Headphones  => "\uE7EF",   // Headphone
            DeviceType.Phone       => "\uE8EA",   // CellPhone
            DeviceType.Computer    => "\uE7F4",   // Laptop
            DeviceType.Speaker     => "\uE767",   // Volume
            DeviceType.Keyboard    => "\uE92E",   // Keyboard
            DeviceType.Mouse       => "\uE962",   // Mouse
            _                      => "\uE702",   // Bluetooth
        };

        public static string GetLabel(DeviceType type) => type switch
        {
            DeviceType.Earbuds    => "Earbuds",
            DeviceType.Headphones => "Headphones",
            DeviceType.Phone      => "Phone",
            DeviceType.Computer   => "Computer",
            DeviceType.Speaker    => "Speaker",
            DeviceType.Keyboard   => "Keyboard",
            DeviceType.Mouse      => "Mouse",
            _                     => "Device",
        };
    }
}
