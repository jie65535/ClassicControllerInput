using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Zorro.ControllerSupport;

namespace ClassicControllerInput;

[HarmonyPatch]
internal static class ControllerPatches
{
    private static readonly FieldInfo ClimbingCharacter = AccessTools.Field(typeof(CharacterClimbing), "character");
    private static readonly FieldInfo MovementCharacter = AccessTools.Field(typeof(CharacterMovement), "character");
    private static readonly FieldInfo ClimbToggledOn = AccessTools.Field(typeof(CharacterClimbing), "climbToggledOn");
    private static readonly ConditionalWeakTable<Character, object> ClassicClimbs = new();
    private static readonly object ClassicClimb = new();

    [HarmonyPatch(typeof(CharacterClimbing), "TryToStartWallClimb")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> ChooseClimbAtGrab(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        int setting = FindSettingValue<ClimbInputSetting>(code);
        code.InsertRange(setting + 1, new[]
        {
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldfld, ClimbingCharacter),
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldflda, ClimbToggledOn),
            CodeInstruction.Call(typeof(ControllerPatches), nameof(GetClimbMode))
        });
        return code;
    }

    [HarmonyPatch(typeof(CharacterClimbing), "Update")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> PreserveClassicGrab(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        FilterButtonReads(code, nameof(CharacterInput.usePrimaryWasPressed), ClimbingCharacter,
            nameof(AllowClimbButtonDetach));
        return code;
    }

    [HarmonyPatch(typeof(CharacterMovement), "SetMovementState")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> RestoreSprintToggle(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        int setting = FindSettingValue<SprintInputSetting>(code);
        code.InsertRange(setting + 1, new[]
        {
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldfld, MovementCharacter),
            CodeInstruction.Call(typeof(ControllerPatches), nameof(GetSprintMode))
        });
        FilterButtonReads(code, nameof(CharacterInput.sprintToggleIsPressed), MovementCharacter,
            nameof(AllowSprintButtonHold));
        return code;
    }

    private static int FindSettingValue<TSetting>(List<CodeInstruction> code)
    {
        var matches = code.Select((instruction, index) => (instruction, index))
            .Where(entry => entry.instruction.operand is MethodInfo method
                && method.DeclaringType == typeof(SettingsHandler)
                && method.Name == nameof(SettingsHandler.GetSetting)
                && method.IsGenericMethod
                && method.GetGenericArguments()[0] == typeof(TSetting))
            .Select(entry => entry.index + 1)
            .ToArray();
        if (matches.Length == 1 && matches[0] + 2 < code.Count
            && code[matches[0]].operand is MethodInfo { Name: "get_Value" }
            && code[matches[0] + 1].opcode == OpCodes.Ldc_I4_1
            && (code[matches[0] + 2].opcode == OpCodes.Bne_Un_S || code[matches[0] + 2].opcode == OpCodes.Bne_Un))
        {
            return matches[0];
        }

        throw new InvalidOperationException($"Could not locate the {typeof(TSetting).Name} comparison.");
    }

    private static void FilterButtonReads(List<CodeInstruction> code, string fieldName, FieldInfo character, string filter)
    {
        var button = AccessTools.Field(typeof(CharacterInput), fieldName);
        var reads = code.Select((instruction, index) => (instruction, index))
            .Where(entry => entry.instruction.LoadsField(button))
            .Select(entry => entry.index)
            .Reverse()
            .ToArray();
        if (reads.Length != 2)
        {
            throw new InvalidOperationException($"Expected two reads of {fieldName} and found {reads.Length}.");
        }

        foreach (int read in reads)
        {
            code.InsertRange(read + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, character),
                CodeInstruction.Call(typeof(ControllerPatches), filter)
            });
        }
    }

    private static ClimbInputSetting.InputMode GetClimbMode(ClimbInputSetting.InputMode mode, Character character,
        ref bool climbToggledOn)
    {
        bool classic = mode == ClassicClimbInputSetting.Classic;
        if (classic && character.IsLocal && InputHandler.GetCurrentUsedInputScheme() == InputScheme.Gamepad)
        {
            ClassicClimbs.GetValue(character, static _ => ClassicClimb);
            return ClimbInputSetting.InputMode.SettingToggle;
        }

        // Transferring to a rope or vine can end a wall climb without calling StopClimbingRpc.
        if (ClassicClimbs.Remove(character) && mode != ClimbInputSetting.InputMode.SettingToggle)
        {
            climbToggledOn = false;
        }

        return classic ? ClimbInputSetting.InputMode.SettingHold : mode;
    }

    private static bool AllowClimbButtonDetach(bool pressed, Character character) =>
        pressed && !ClassicClimbs.TryGetValue(character, out _);

    private static SprintInputSetting.InputMode GetSprintMode(SprintInputSetting.InputMode mode, Character character)
    {
        if (mode != ClassicSprintInputSetting.Classic)
        {
            return mode;
        }

        return character.IsLocal && character.input.sprintToggleWasPressed
            ? SprintInputSetting.InputMode.SettingToggle
            : SprintInputSetting.InputMode.SettingHold;
    }

    private static bool AllowSprintButtonHold(bool held, Character character) =>
        held && (!character.IsLocal
            || SettingsHandler.Instance.GetSetting<SprintInputSetting>().Value != ClassicSprintInputSetting.Classic);
}
