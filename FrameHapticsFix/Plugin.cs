using System.Reflection;
using HarmonyLib;
using IPA;
using IPA.Loader;
using IpaLogger = IPA.Logging.Logger;

namespace FrameHapticsFix;

[Plugin(RuntimeOptions.DynamicInit)]
internal class Plugin
{
    internal static IpaLogger Log { get; private set; } = null!;

    private Harmony _harmony;
    private Assembly _executingAssembly = Assembly.GetExecutingAssembly();

    // Methods with [Init] are called when the plugin is first loaded by IPA.
    // All the parameters are provided by IPA and are optional.
    // The constructor is called before any method with [Init]. Only use [Init] with one constructor.
    [Init]
    public Plugin(IpaLogger ipaLogger, PluginMetadata pluginMetadata)
    {
        Log = ipaLogger;
        _harmony = new Harmony(pluginMetadata.Id);
        Log.Info($"{pluginMetadata.Name} {pluginMetadata.HVersion} initialized.");
    }

    [OnStart]
    public void OnApplicationStart()
    {
        Log.Debug("OnApplicationStart");
        _harmony.PatchAll(_executingAssembly);
    }

    [OnExit]
    public void OnApplicationQuit()
    {
        _harmony.UnpatchSelf();
        Log.Debug("OnApplicationQuit");
    }
}