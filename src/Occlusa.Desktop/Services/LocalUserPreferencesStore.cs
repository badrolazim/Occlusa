using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.Services;

/// <summary>
/// Offline-safe local JSON mirror of per-staff workspace preferences stored in %LocalAppData%\Occlusa\user-preferences.json.
/// </summary>
public static class LocalUserPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Occlusa",
        "user-preferences.json");

    public static Dictionary<string, UserWorkspacePreferencesDto> LoadAll()
    {
        var map = new Dictionary<string, UserWorkspacePreferencesDto>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(FilePath))
            {
                return map;
            }

            var json = File.ReadAllText(FilePath);
            var list = JsonSerializer.Deserialize<List<UserWorkspacePreferencesDto>>(json, JsonOptions);
            if (list is null)
            {
                return map;
            }

            foreach (var pref in list)
            {
                var key = AppointmentCalendarHelper.NormalizeStaffKey(
                    string.IsNullOrWhiteSpace(pref.StaffKey) ? pref.StaffDisplayName : pref.StaffKey);
                pref.StaffKey = key;
                map[key] = pref;
            }
        }
        catch
        {
            // Ignore malformed local cache and fall back to server or role defaults.
        }

        return map;
    }

    public static void SaveAll(IEnumerable<UserWorkspacePreferencesDto> items)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(items.ToList(), JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Non-fatal offline cache failure.
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        opts.Converters.Add(new JsonStringEnumConverter());
        return opts;
    }
}

