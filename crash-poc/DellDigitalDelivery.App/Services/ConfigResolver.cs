namespace DellDigitalDelivery.App.Services;

/// <summary>
/// Resolves configuration values with support for nested references.
/// BUG: ResolveRecursive has no depth limit, causing infinite recursion
/// (StackOverflowException) when config contains circular references.
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
        _config["app.cache_dir"] = "${app.data_dir}/cache"; // BUG: circular reference
        _config["app.log_level"] = "Info";
    }

    /// <summary>
    /// Resolves a config key, expanding any ${...} references.
    /// BUG: No cycle detection — circular references cause deep recursion
    /// that eventually throws an InvalidOperationException when depth exceeds limit.
    /// In a real app without the safety limit, this would be a StackOverflowException.
    /// </summary>
    public string ResolveRecursive(string key, int depth = 0)
    {
        // Safety limit so we can catch the error (real StackOverflow is uncatchable)
        // BUG: The real issue is there's no cycle detection — this should track visited keys
        if (depth > 100)
            throw new InvalidOperationException(
                $"Stack overflow detected: circular config reference at key '{key}' (depth={depth}). " +
                "ConfigResolver.ResolveRecursive has no cycle detection for ${...} references.");

        if (!_config.TryGetValue(key, out var value))
            return string.Empty;

        // Expand ${...} references
        while (value.Contains("${"))
        {
            int start = value.IndexOf("${", StringComparison.Ordinal);
            int end = value.IndexOf('}', start);
            if (end < 0) break;

            string refKey = value.Substring(start + 2, end - start - 2);
            // BUG: No cycle detection — this recurses for circular refs
            string resolved = ResolveRecursive(refKey, depth + 1);
            value = value[..start] + resolved + value[(end + 1)..];
        }

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
