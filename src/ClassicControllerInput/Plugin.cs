using System;
using BepInEx;
using HarmonyLib;

namespace ClassicControllerInput;

[BepInPlugin(PluginGuid, "ClassicControllerInput", "0.1.0")]
public sealed class Plugin : BaseUnityPlugin
{
    internal const string PluginGuid = "com.github.jie65535.ClassicControllerInput";
    private Harmony? harmony;

    private void Awake()
    {
        harmony = new Harmony(PluginGuid);
        try
        {
            harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo("ClassicControllerInput ready. Classic is available in the climb and sprint settings.");
        }
        catch (Exception exception)
        {
            harmony.UnpatchSelf();
            harmony = null;
            Logger.LogError($"Could not apply the controller patches to this game build. {exception}");
        }
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
    }
}
