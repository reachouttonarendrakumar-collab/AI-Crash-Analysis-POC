namespace DellDigitalDelivery.App.Services;

/// <summary>
/// Resolves configuration values with support for nested references.
/// </summary>
public class ConfigResolver
{
    private readonly Dictionary<string, string> _config = new();

    public ConfigResolver()
    {
        // Seed with config that contains a circular reference
        _config["app.name"] = "DellDigitalDelivery";
        _config["app.version"] = "3.9.1000.0";
        _config["app.data_dir"] = "${app.cache_dir}/data";
        _config["app.cache_dir"] = "${app.data_dir}/cache"; // Circular reference
        _config["app.log_level"] = "Info";
    }

    /// <summary>
    /// Resolves a config key, expanding any ${...} references.
    /// Implements cycle detection to prevent infinite recursion.
    /// </summary>
    public string ResolveRecursive(string key, HashSet<string> visitedKeys = null)
    {
        visitedKeys ??= new HashSet<string>();

        // Check for circular reference
        if (visitedKeys.Contains(key))
            throw new InvalidOperationException(
                $"Circular config reference detected at key '{key}'.");

        visitedKeys.Add(key);

        if (!_config.TryGetValue(key, out var value))
            return string.Empty;

        // Expand ${...} references
        while (value.Contains("${"))
        {
            int start = value.IndexOf("${", StringComparison.Ordinal);
            int end = value.IndexOf('}', start);
            if (end < 0) break;

            string refKey = value.Substring(start + 2, end - start - 2);
            string resolved = ResolveRecursive(refKey, visitedKeys);
            value = value[..start] + resolved + value[(end + 1)..];
        }

        visitedKeys.Remove(key);
        return value;
    }

    /// <summary>
    /// Loads the unified agent configuration.
    /// </summary>
    public Dictionary<string, string> Load(string path)
    {
        Console.WriteLine($"[ConfigResolver] Loading config from: {path}");
        var resolved = new Dictionary<string, string>();

        foreach (var (key, _) in _config)
        {
            resolved[key] = ResolveRecursive(key);
        }

        return resolved;
    }
}