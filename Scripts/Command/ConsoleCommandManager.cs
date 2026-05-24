using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;

namespace BetterConsole.Command;

internal static class ConsoleCommandManager
{
    internal readonly record struct CommandSegment(string Command, bool RequiresPreviousSuccess);

    private sealed record IndexRule(int ArgIndex, Func<Player?, int?> CountProvider, Func<string[], bool>? CanApply = null);

    private sealed record TitleRule(string Table, string Suffix = ".title");

    private static readonly Dictionary<string, IndexRule> IndexRules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["upgrade"] = new(0, HandCount),
        ["afflict"] = new(2, HandCount),
        ["enchant"] = new(2, HandCount),
        ["damage"] = new(1, CreatureCount),
        ["block"] = new(1, CreatureCount),
        ["power"] = new(2, CreatureCount),
        ["heal"] = new(1, AllyCount),
        ["kill"] = new(0, EnemyCount, args => args.Length == 0 || !args[0].Equals("all", StringComparison.OrdinalIgnoreCase)),
    };

    private static readonly Dictionary<string, TitleRule[]> TitleRules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["card"] = [new("cards")],
        ["remove_card"] = [new("cards")],
        ["relic"] = [new("relics")],
        ["potion"] = [new("potions")],
        ["power"] = [new("powers")],
        ["orb"] = [new("orbs")],
        ["afflict"] = [new("afflictions")],
        ["enchant"] = [new("enchantments")],
        ["event"] = [new("events")],
        ["ancient"] = [new("ancients")],
        ["fight"] = [new("encounters")],
        ["act"] = [new("acts")],
        ["monster"] = [new("monsters", ".name")],
    };

    private static readonly Dictionary<(string Language, string Command, string CompletedArgs, string Candidate), string[]> TitleCache = [];

    internal static IReadOnlyList<CommandSegment> SplitChain(string inputValue)
    {
        var segments = new List<CommandSegment>();
        var quote = false;
        var requireSuccess = false;
        var start = 0;

        for (var i = 0; i <= inputValue.Length; i++)
        {
            var atEnd = i == inputValue.Length;
            var atOp = false;
            var nextRequireSuccess = false;
            var opLen = 0;

            if (!atEnd)
            {
                if (inputValue[i] == '"')
                    quote = !quote;
                else if (!quote && inputValue[i] == '&')
                {
                    atOp = true;
                    nextRequireSuccess = i + 1 < inputValue.Length && inputValue[i + 1] == '&';
                    opLen = nextRequireSuccess ? 2 : 1;
                }
            }

            if (!atEnd && !atOp)
                continue;

            var command = inputValue[start..i].Trim();
            if (command.Length > 0)
                segments.Add(new CommandSegment(command, requireSuccess));

            if (!atOp)
                continue;

            requireSuccess = nextRequireSuccess;
            i += opLen - 1;
            start = i + 1;
        }

        return segments;
    }

    internal static bool TryGetCompletionTail(string inputValue, out string prefix, out string tail)
    {
        var quote = false;
        var split = -1;

        for (var i = 0; i < inputValue.Length; i++)
        {
            if (inputValue[i] == '"')
                quote = !quote;
            else if (!quote && inputValue[i] == '&')
            {
                split = i;
                if (i + 1 < inputValue.Length && inputValue[i + 1] == '&')
                    i++;
            }
        }

        if (split < 0)
        {
            prefix = "";
            tail = inputValue;
            return false;
        }

        var tailStart = split + 1;
        if (split + 1 < inputValue.Length && inputValue[split + 1] == '&')
            tailStart++;

        while (tailStart < inputValue.Length && char.IsWhiteSpace(inputValue[tailStart]))
            tailStart++;

        prefix = inputValue[..tailStart];
        tail = inputValue[tailStart..];
        return true;
    }

    internal static string NormalizeForCompatibility(Player? player, string command)
    {
        command = command.Trim();
        if (command.Length == 0)
            return command;

        var tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || !IndexRules.TryGetValue(tokens[0], out var rule))
            return command;

        var args = tokens[1..];
        if (args.Length <= rule.ArgIndex || rule.CanApply?.Invoke(args) == false || !int.TryParse(args[rule.ArgIndex], out var value) || value >= 0)
            return command;

        var count = rule.CountProvider(player);
        if (count == null)
            return command;

        var index = count.Value + value;
        if (index < 0 || index >= count.Value)
            return command;

        args[rule.ArgIndex] = index.ToString();
        return tokens[0] + (args.Length > 0 ? " " + string.Join(" ", args) : "");
    }

    internal static string[] GetLocalizedCandidateTitles(string commandName, string[] completedArgs, string candidate)
    {
        var cacheKey = (LocManager.Instance.Language, commandName, string.Join("\u001f", completedArgs), candidate);
        if (TitleCache.TryGetValue(cacheKey, out var cachedTitles))
            return cachedTitles;

        if (!TitleRules.TryGetValue(commandName, out var rules))
            return TitleCache[cacheKey] = [];

        return TitleCache[cacheKey] = [.. rules.Select(rule => TryGetRawLocText(rule.Table, candidate + rule.Suffix))
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(title => title!)];
    }

    private static int? HandCount(Player? player) => player == null ? null : PileType.Hand.GetPile(player).Cards.Count;

    private static int? CreatureCount(Player? _) => CombatManager.Instance.IsInProgress ? CombatManager.Instance.DebugOnlyGetState()?.Creatures.Count : null;

    private static int? AllyCount(Player? _) => CombatManager.Instance.IsInProgress ? CombatManager.Instance.DebugOnlyGetState()?.Allies.Count : null;

    private static int? EnemyCount(Player? _) => CombatManager.Instance.IsInProgress ? CombatManager.Instance.DebugOnlyGetState()?.Enemies.Count : null;

    private static string? TryGetRawLocText(string table, string key)
    {
        try
        {
            return LocString.Exists(table, key) ? new LocString(table, key).GetRawText() : null;
        }
        catch
        {
            return null;
        }
    }
}
