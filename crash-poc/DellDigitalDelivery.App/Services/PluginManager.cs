namespace DellDigitalDelivery.App.Services;

/// <summary>
/// Manages loading and initialization of delivery plugins.
/// BUG: LoadPlugin does not null-check the plugin instance returned
/// by the factory, causing a NullReferenceException (Access Violation).
/// </summary>
public class PluginManager
{
    private readonly Dictionary<string, object> _loadedPlugins = new();

    /// <summary>
    /// Loads a plugin by path and initializes it.
    /// BUG: pluginInstance is null when path does not match a known plugin,
    /// but we call .ToString() on it without checking, causing NullReferenceException.
    /// </summary>
    public void LoadPlugin(string pluginPath)
    {
        if (string.IsNullOrWhiteSpace(pluginPath))
            throw new ArgumentException("Plugin path is required.", nameof(pluginPath));

        // Simulate plugin factory that returns null for unknown plugins
        object? pluginInstance = CreatePluginInstance(pluginPath);

        // BUG: No null check — causes NullReferenceException
        string pluginName = pluginInstance!.GetType().Name;
        _loadedPlugins[pluginName] = pluginInstance;

        Console.WriteLine($"[PluginManager] Loaded plugin: {pluginName} from {pluginPath}");
    }

    /// <summary>
    /// Initializes all loaded plugins.
    /// </summary>
    public void InitializePlugins()
    {
        foreach (var (name, plugin) in _loadedPlugins)
        {
            Console.WriteLine($"[PluginManager] Initializing: {name}");
        }
    }

    /// <summary>
    /// Factory method that returns null for unknown plugin paths.
    /// This is the root cause — it should throw instead of returning null.
    /// </summary>
    private static object? CreatePluginInstance(string pluginPath)
    {
        // BUG: Returns null for paths that don't match known plugins
        // instead of throwing an exception
        return pluginPath switch
        {
            "plugins/delivery.dll" => new DeliveryPlugin(),
            "plugins/update.dll" => new UpdatePlugin(),
            _ => null  // BUG: Should throw PluginNotFoundException
        };
    }

    private class DeliveryPlugin { }
    private class UpdatePlugin { }
}
