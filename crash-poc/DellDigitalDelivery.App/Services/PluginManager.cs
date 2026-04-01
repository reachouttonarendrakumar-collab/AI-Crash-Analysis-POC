namespace DellDigitalDelivery.App.Services;

/// <summary>
/// Manages loading and initialization of delivery plugins.
/// </summary>
public class PluginManager
{
    private readonly Dictionary<string, object> _loadedPlugins = new();

    /// <summary>
    /// Loads a plugin by path and initializes it.
    /// </summary>
    public void LoadPlugin(string pluginPath)
    {
        if (string.IsNullOrWhiteSpace(pluginPath))
            throw new ArgumentException("Plugin path is required.", nameof(pluginPath));

        // Simulate plugin factory that throws for unknown plugins
        object pluginInstance = CreatePluginInstance(pluginPath);

        // Ensure pluginInstance is not null
        if (pluginInstance == null)
        {
            throw new InvalidOperationException($"Failed to load plugin from path: {pluginPath}");
        }

        string pluginName = pluginInstance.GetType().Name;
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
    /// Factory method that throws an exception for unknown plugin paths.
    /// </summary>
    private static object CreatePluginInstance(string pluginPath)
    {
        return pluginPath switch
        {
            "plugins/delivery.dll" => new DeliveryPlugin(),
            "plugins/update.dll" => new UpdatePlugin(),
            _ => throw new PluginNotFoundException($"Plugin not found for path: {pluginPath}")
        };
    }

    private class DeliveryPlugin { }
    private class UpdatePlugin { }
}

/// <summary>
/// Exception thrown when a plugin cannot be found.
/// </summary>
public class PluginNotFoundException : Exception
{
    public PluginNotFoundException(string message) : base(message) { }
}