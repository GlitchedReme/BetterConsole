using BetterConsole.Command;
using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Debug;
using TinyPinyin;

namespace BetterConsole.Patch;

[HarmonyPatch]
internal static class ConsolePinyinPatch
{
    [ThreadStatic]
    private static int _updateGhostTextDepth;

    [HarmonyPatch(typeof(AbstractConsoleCmd), "CompleteArgument")]
    [HarmonyPrefix]
    public static void WrapPredicatePrefix(AbstractConsoleCmd __instance, string[] completedArgs, ref Func<string, string, bool>? matchPredicate)
    {
        if (!LocManager.Instance.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return;

        var inner = matchPredicate;

        matchPredicate = (candidate, partial) =>
        {
            if (inner?.Invoke(candidate, partial) == true)
                return true;

            return ConsoleCommandManager.GetLocalizedCandidateTitles(__instance.CmdName, completedArgs, candidate).Prepend(candidate)
                .Any(text => TextMatches(text, partial));
        };
    }

    [HarmonyPatch(typeof(NDevConsole), "UpdateGhostText")]
    [HarmonyPrefix]
    public static void UpdateGhostTextPrefix() => _updateGhostTextDepth++;

    [HarmonyPatch(typeof(NDevConsole), "UpdateGhostText")]
    [HarmonyPostfix]
    public static void UpdateGhostTextPostfix() => _updateGhostTextDepth = Math.Max(0, _updateGhostTextDepth - 1);

    [HarmonyPatch(typeof(DevConsole), nameof(DevConsole.GetCompletionResults))]
    [HarmonyPostfix]
    public static void GetCompletionResultsPostfix(string inputBuffer, CompletionResult __result)
    {
        if (__result.Candidates.Count == 1 && !string.IsNullOrEmpty(__result.CommonPrefix) && !__result.CommonPrefix.StartsWith(inputBuffer, StringComparison.OrdinalIgnoreCase) && _updateGhostTextDepth > 0)
            __result.CommonPrefix = BuildGhostCommonPrefix(inputBuffer, __result);
    }

    private static bool TextMatches(string text, string partial)
    {
        if (text.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            return true;

        var partialKey = NormalizePinyinKey(partial);
        if (partialKey.Length == 0)
            return false;

        var pinyin = NormalizePinyinKey(PinyinHelper.GetPinyin(text, ""));
        if (pinyin.StartsWith(partialKey, StringComparison.OrdinalIgnoreCase))
            return true;

        return NormalizePinyinKey(PinyinHelper.GetPinyinInitials(text)).StartsWith(partialKey, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePinyinKey(string value) =>
        string.IsNullOrWhiteSpace(value) ? "" : string.Concat(value.Where(char.IsLetterOrDigit));

    private static string BuildGhostCommonPrefix(string inputBuffer, CompletionResult result)
    {
        var displayCandidate = result.Candidates[0];
        var candidate = GetCandidateId(displayCandidate);
        var title = ConsoleCommandManager.GetLocalizedCandidateTitles(result.ArgumentContext, [], candidate).FirstOrDefault();
        var titlePinyin = title == null ? "" : NormalizePinyinKey(PinyinHelper.GetPinyin(title, "")).ToLowerInvariant();
        if (titlePinyin.Length == 0)
            return inputBuffer;

        var argumentStart = inputBuffer.LastIndexOf(' ') + 1;
        var commandPrefix = argumentStart > 0 ? inputBuffer[..argumentStart] : "";
        return $"{commandPrefix}{titlePinyin} -> {displayCandidate} ";
    }

    private static string GetCandidateId(string candidate) => candidate.Split(' ', 2)[0];
}
