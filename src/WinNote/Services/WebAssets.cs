using System.IO;
using System.Reflection;

namespace WinNote.Services;

public static class WebAssets
{
    public static string Extract()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "data", "web");
        Directory.CreateDirectory(dir);

        var assembly = Assembly.GetExecutingAssembly();
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            const string marker = ".wwwroot.";
            var index = resourceName.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var relative = resourceName[(index + marker.Length)..];
            var destination = Path.Combine(dir, relative);
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            using var input = assembly.GetManifestResourceStream(resourceName);
            if (input is null)
            {
                continue;
            }

            using var output = File.Create(destination);
            input.CopyTo(output);
        }

        return dir;
    }
}
