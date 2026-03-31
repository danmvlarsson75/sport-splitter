using System.Text.Json;

namespace SportSplitter;

public class Config
{
    public string[] Urls { get; set; } = ["", "", "", "", "", "", "", "", ""];
    public bool AudioFollowsMouse { get; set; } = false;

    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WebSplitter", "config.json");

    public static Config Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var loaded = JsonSerializer.Deserialize<Config>(File.ReadAllText(_path));
                if (loaded != null)
                {
                    // Ensure Urls always has exactly 4 slots
                    if (loaded.Urls.Length < 9)
                    {
                        var padded = Enumerable.Repeat("", 9).ToArray();
                        for (int i = 0; i < loaded.Urls.Length; i++)
                            padded[i] = loaded.Urls[i] ?? "";
                        loaded.Urls = padded;
                    }
                    // Sanitize any nulls
                    for (int i = 0; i < loaded.Urls.Length; i++)
                        loaded.Urls[i] ??= "";
                    return loaded;
                }
            }
        }
        catch { }
        return new Config();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
