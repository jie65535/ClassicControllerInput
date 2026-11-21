using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Zorro.Settings;

namespace ClassicControllerInput;

[HarmonyPatch(typeof(SettingsHandler), MethodType.Constructor)]
internal static class InputSettingsPatch
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> InstallInputSettings(IEnumerable<CodeInstruction> instructions)
    {
        int replacements = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Newobj && instruction.operand is ConstructorInfo constructor)
            {
                if (constructor.DeclaringType == typeof(ClimbInputSetting))
                {
                    instruction.operand = AccessTools.Constructor(typeof(ClassicClimbInputSetting));
                    replacements++;
                }
                else if (constructor.DeclaringType == typeof(SprintInputSetting))
                {
                    instruction.operand = AccessTools.Constructor(typeof(ClassicSprintInputSetting));
                    replacements++;
                }
            }

            yield return instruction;
        }

        if (replacements != 2)
        {
            throw new InvalidOperationException("Could not locate the two control settings.");
        }
    }
}

internal sealed class ClassicClimbInputSetting : ClimbInputSetting
{
    internal const InputMode Classic = (InputMode)2;

    protected override InputMode GetDefaultValue() => Classic;

    public override void Load(ISettingsSaveLoad loader) =>
        Value = ClassicInputOptions.Load<ClimbInputSetting, InputMode>(loader, GetDefaultValue());

    public override void Save(ISettingsSaveLoad saver) =>
        ClassicInputOptions.Save<ClimbInputSetting, InputMode>(saver, Value, Classic);

    public override List<string> GetUnlocalizedChoices() =>
        ClassicInputOptions.AddChoice(base.GetUnlocalizedChoices(), localize: false);

    public override List<string> GetCustomLocalizedChoices() =>
        ClassicInputOptions.AddChoice(base.GetUnlocalizedChoices(), localize: true);
}

internal sealed class ClassicSprintInputSetting : SprintInputSetting
{
    internal const InputMode Classic = (InputMode)2;

    protected override InputMode GetDefaultValue() => Classic;

    public override void Load(ISettingsSaveLoad loader) =>
        Value = ClassicInputOptions.Load<SprintInputSetting, InputMode>(loader, GetDefaultValue());

    public override void Save(ISettingsSaveLoad saver) =>
        ClassicInputOptions.Save<SprintInputSetting, InputMode>(saver, Value, Classic);

    public override List<string> GetUnlocalizedChoices() =>
        ClassicInputOptions.AddChoice(base.GetUnlocalizedChoices(), localize: false);

    public override List<string> GetCustomLocalizedChoices() =>
        ClassicInputOptions.AddChoice(base.GetUnlocalizedChoices(), localize: true);
}

internal static class ClassicInputOptions
{
    internal static TMode Load<TSetting, TMode>(ISettingsSaveLoad loader, TMode classic)
        where TSetting : EnumSetting<TMode>
        where TMode : unmanaged, Enum
    {
        Type settingType = typeof(TSetting);
        if (loader.TryLoadBool(GetClassicKey(settingType), out bool useClassic) && useClassic)
        {
            return classic;
        }

        if (loader.TryLoadString(settingType, out string stored)
            && Enum.TryParse(stored, out TMode nativeMode)
            && Enum.IsDefined(typeof(TMode), nativeMode))
        {
            return nativeMode;
        }

        return classic;
    }

    internal static void Save<TSetting, TMode>(ISettingsSaveLoad saver, TMode mode, TMode classic)
        where TSetting : EnumSetting<TMode>
        where TMode : unmanaged, Enum
    {
        bool useClassic = EqualityComparer<TMode>.Default.Equals(mode, classic);
        if (!useClassic)
        {
            saver.SaveString(typeof(TSetting), mode.ToString());
        }

        saver.SaveBool(GetClassicKey(typeof(TSetting)), useClassic);
    }

    internal static List<string> AddChoice(List<string> choices, bool localize)
    {
        string label = "Classic";
        if (localize)
        {
            for (int index = 0; index < choices.Count; index++)
            {
                choices[index] = LocalizedText.GetText(choices[index]);
            }

            label = LocalizedText.CURRENT_LANGUAGE switch
            {
                LocalizedText.Language.SimplifiedChinese => "经典",
                LocalizedText.Language.TraditionalChinese => "經典",
                _ => label
            };
        }

        choices.Add(label);
        return choices;
    }

    private static string GetClassicKey(Type settingType) => $"{Plugin.PluginGuid}.{settingType.Name}";
}
