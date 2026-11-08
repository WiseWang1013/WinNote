using System.IO;
using System.Text;
using System.Text.Json;
using WinNote.Models;

namespace WinNote.Services;

public sealed class ProfileInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class ProfileDocument
{
    public string ActiveId { get; set; } = "";
    public List<ProfileInfo> Profiles { get; set; } = new();
}

public sealed class ProfileStore
{
    private readonly string _configPath;

    public ProfileStore()
    {
        _configPath = ResolveConfigPath();
        Load();
        EnsureDefault();
        Save();
    }

    public List<ProfileInfo> Profiles { get; private set; } = new();
    public string ActiveId { get; private set; } = "";

    public ProfileInfo Active =>
        Profiles.FirstOrDefault(profile => profile.Id == ActiveId)
        ?? Profiles.First();

    public ProfileInfo SetActive(string id)
    {
        var profile = Profiles.FirstOrDefault(item => item.Id == id)
                      ?? throw new InvalidOperationException("找不到这个数据文件夹。");
        ActiveId = profile.Id;
        Save();
        return profile;
    }

    public ProfileInfo Add(string name, string? path = null)
    {
        name = string.IsNullOrWhiteSpace(name) ? "新数据文件夹" : name.Trim();
        path = string.IsNullOrWhiteSpace(path)
            ? CreateDefaultPath(name)
            : System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(path);

        var profile = new ProfileInfo
        {
            Id = "p_" + Guid.NewGuid().ToString("N")[..10],
            Name = UniqueName(name),
            Path = path,
        };
        Profiles.Add(profile);
        ActiveId = profile.Id;
        Save();
        return profile;
    }

    public ProfileInfo Rename(string id, string name)
    {
        var profile = Profiles.FirstOrDefault(item => item.Id == id)
                      ?? throw new InvalidOperationException("找不到这个数据文件夹。");
        name = string.IsNullOrWhiteSpace(name) ? profile.Name : name.Trim();
        profile.Name = UniqueName(name, profile.Id);
        Save();
        return profile;
    }

    public string CreateDefaultPath(string name)
    {
        var baseDirectory = System.IO.Path.GetDirectoryName(_configPath) ?? AppContext.BaseDirectory;
        var safe = new string(name
            .Where(ch => !System.IO.Path.GetInvalidFileNameChars().Contains(ch) && ch != '\\' && ch != '/')
            .ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "数据";
        }

        var candidate = System.IO.Path.Combine(baseDirectory, "data-" + safe);
        var index = 2;
        while (Directory.Exists(candidate) || File.Exists(candidate))
        {
            candidate = System.IO.Path.Combine(baseDirectory, $"data-{safe}-{index}");
            index++;
        }
        return candidate;
    }

    private string UniqueName(string name, string? exceptId = null)
    {
        var candidate = name;
        var index = 2;
        while (Profiles.Any(profile => profile.Id != exceptId
                                       && string.Equals(profile.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{name} {index}";
            index++;
        }
        return candidate;
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath, Encoding.UTF8);
                var document = JsonSerializer.Deserialize<ProfileDocument>(json, JsonDefaults.Options);
                if (document?.Profiles is { Count: > 0 })
                {
                    Profiles = document.Profiles;
                    ActiveId = document.ActiveId;
                }
            }
        }
        catch
        {
            try
            {
                File.Move(_configPath, _configPath + ".bad-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
            }
            catch
            {
                // ignored
            }
        }

        Profiles = Profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Id)
                              && !string.IsNullOrWhiteSpace(profile.Path))
            .ToList();
    }

    private void EnsureDefault()
    {
        var legacy = System.IO.Path.Combine(AppContext.BaseDirectory, "data");
        if (Profiles.Count == 0 && Directory.Exists(legacy))
        {
            Profiles.Add(new ProfileInfo
            {
                Id = "default",
                Name = "默认数据（data）",
                Path = legacy,
            });
            ActiveId = "default";
        }

        if (Profiles.Count == 0)
        {
            var path = CreateDefaultPath("私人");
            Directory.CreateDirectory(path);
            Profiles.Add(new ProfileInfo
            {
                Id = "default",
                Name = "私人",
                Path = path,
            });
            ActiveId = "default";
        }

        foreach (var profile in Profiles)
        {
            profile.Name = string.IsNullOrWhiteSpace(profile.Name)
                ? System.IO.Path.GetFileName(profile.Path)
                : profile.Name.Trim();
            profile.Path = System.IO.Path.GetFullPath(profile.Path);
            try
            {
                Directory.CreateDirectory(profile.Path);
            }
            catch
            {
                // ignored
            }
        }

        if (Profiles.All(profile => profile.Id != ActiveId))
        {
            ActiveId = Profiles[0].Id;
        }
    }

    private void Save()
    {
        var document = new ProfileDocument
        {
            ActiveId = ActiveId,
            Profiles = Profiles,
        };
        var json = JsonSerializer.Serialize(document, JsonDefaults.Options);
        var temp = _configPath + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        File.Move(temp, _configPath, true);
    }

    private static string ResolveConfigPath()
    {
        var portable = System.IO.Path.Combine(AppContext.BaseDirectory, "profiles.json");
        try
        {
            var probe = portable + ".probe";
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return portable;
        }
        catch
        {
            var folder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinNote");
            Directory.CreateDirectory(folder);
            return System.IO.Path.Combine(folder, "profiles.json");
        }
    }
}
