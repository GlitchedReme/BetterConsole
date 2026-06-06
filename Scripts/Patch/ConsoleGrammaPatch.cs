using System.Text;
using BetterConsole.Command;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Runs;

namespace BetterConsole.Patch;

[HarmonyPatch]
public class ConsoleGrammaPatch
{
    private static readonly System.Reflection.MethodInfo SaveCommandHistoryMethod = AccessTools.Method(typeof(DevConsole), "SaveCommandHistory");
    private static readonly System.Reflection.MethodInfo ProcessCommandInternalMethod = AccessTools.Method(typeof(DevConsole), "ProcessCommandInternal");

    [HarmonyPatch(typeof(DevConsole), nameof(DevConsole.GetCompletionResults))]
    public static bool Prefix(DevConsole __instance, string inputBuffer, ref CompletionResult __result)
    {
        if (!ConsoleCommandManager.TryGetCompletionTail(inputBuffer, out var prefix, out var tail))
            return true;

        var result = __instance.GetCompletionResults(tail);

        if (!string.IsNullOrEmpty(result.CommonPrefix))
            result.CommonPrefix = prefix + result.CommonPrefix;

        result.CommandPrefix = prefix + result.CommandPrefix;

        if (result.Type == CompletionType.Command)
        {
            result.Type = CompletionType.Subcommand;
            result.CommandPrefix = prefix;
            result.ArgumentContext = "command";
            result.ArgumentIndex = 0;
        }

        __result = result;
        return false;
    }

    [HarmonyPatch(typeof(DevConsole), nameof(DevConsole.ProcessCommand), [typeof(string)])]
    [HarmonyPrefix]
    public static bool ProcessCommandPrefix(DevConsole __instance, string inputValue, Dictionary<string, AbstractConsoleCmd> ____commands, ref CmdResult __result)
    {
        inputValue = inputValue.Trim();
        var issuingPlayer = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState());
        var segments = ConsoleCommandManager.SplitChain(inputValue);

        if (segments.Count == 0)
            return true;

        var normalized = ConsoleCommandManager.NormalizeForCompatibility(issuingPlayer, inputValue);
        if (segments.Count == 1 && normalized == inputValue)
            return true;

        SaveCommandHistory(__instance, inputValue);
        __result = segments.Count == 1
            ? ExecuteOrEnqueue(__instance, ____commands, issuingPlayer, normalized)
            : RunChain(segments, segment => ExecuteOrEnqueue(__instance, ____commands, issuingPlayer, ConsoleCommandManager.NormalizeForCompatibility(issuingPlayer, segment)));
        return false;
    }


    [HarmonyPatch(typeof(DevConsole), nameof(DevConsole.ProcessNetCommand))]
    [HarmonyPrefix]
    public static bool ProcessNetCommandPrefix(DevConsole __instance, Player? player, string inputValue, ref CmdResult __result)
    {
        inputValue = inputValue.Trim();
        var segments = ConsoleCommandManager.SplitChain(inputValue);

        if (segments.Count == 0)
            return true;

        var normalized = ConsoleCommandManager.NormalizeForCompatibility(player, inputValue);
        if (segments.Count == 1 && normalized == inputValue)
            return true;

        __result = segments.Count == 1
            ? ExecuteCommandInternal(__instance, player, normalized, runTask: false)
            : RunChain(segments, segment => ExecuteCommandInternal(__instance, player, ConsoleCommandManager.NormalizeForCompatibility(player, segment), runTask: false), includeTask: true);
        return false;
    }

    private static CmdResult RunChain(IReadOnlyList<ConsoleCommandManager.CommandSegment> segments, Func<string, CmdResult> execute, bool includeTask = false)
    {
        var previousSuccess = true;
        var overallSuccess = true;
        var output = new StringBuilder();
        List<Task>? tasks = includeTask ? [] : null;

        foreach (var segment in segments)
        {
            if (segment.RequiresPreviousSuccess && !previousSuccess)
                break;

            var result = execute(segment.Command);
            previousSuccess = result.success;
            overallSuccess &= result.success;

            if (!string.IsNullOrEmpty(result.msg))
            {
                if (output.Length > 0)
                    output.Append('\n');

                output.Append(result.msg);
            }

            if (includeTask && result.task != null)
                tasks!.Add(result.task);
        }

        return tasks is { Count: > 0 }
            ? new CmdResult(RunTasks(tasks), overallSuccess, output.ToString())
            : new CmdResult(overallSuccess, output.ToString());
    }

    private static CmdResult ExecuteOrEnqueue(DevConsole console, Dictionary<string, AbstractConsoleCmd> commands, Player? issuingPlayer, string inputValue)
    {
        var cmdName = inputValue.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "";
        if (!RunManager.Instance.IsSingleplayerOrFakeMultiplayer && commands.TryGetValue(cmdName, out var command) && command.IsNetworked && issuingPlayer != null)
        {
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new ConsoleCmdGameAction(issuingPlayer, inputValue, CombatManager.Instance.IsInProgress));
            return new CmdResult(success: true, $"Enqueued {cmdName} command: '{inputValue}'");
        }

        return ExecuteCommandInternal(console, issuingPlayer, inputValue, runTask: true);
    }

    private static void SaveCommandHistory(DevConsole console, string inputValue)
    {
        console.history.Enqueue(inputValue);
        console.historyIndex = 0;
        SaveCommandHistoryMethod.Invoke(console, []);
    }

    private static CmdResult ExecuteCommandInternal(DevConsole console, Player? player, string inputValue, bool runTask)
    {
        var result = (CmdResult)ProcessCommandInternalMethod.Invoke(console, [player, inputValue.Split(' ')])!;
        if (runTask && result.task != null)
            TaskHelper.RunSafely(result.task);

        return result;
    }

    private static async Task RunTasks(List<Task> tasks)
    {
        foreach (var task in tasks)
            await task;
    }
}
