using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace BetterConsole.Command;

public class OrbConsoleCommand : AbstractConsoleCmd
{
    public override string CmdName => "orb";

    public override string Args => "<id:string>";

    public override string Description => "Spawns the orb with the given ID. Screaming snake case ('LIGHTNING', not 'Lightning').";

    public override bool IsNetworked => true;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (issuingPlayer == null)
        {
            return new CmdResult(success: false, "This command can only be used by a player.");
        }

        if (args.Length < 1)
        {
            return new CmdResult(success: false, CmdName + " requires an orb ID");
        }
        if (!RunManager.Instance.IsInProgress)
        {
            return new CmdResult(success: false, "A run is not in progress.");
        }
        string orbId = args[0].ToUpperInvariant();
        var orbModel = ModelDb.Orbs.FirstOrDefault(o => o.Id.Entry == orbId);
        if (orbModel == null)
        {
            return new CmdResult(success: false, "Orb '" + orbId + "' not found");
        }
        var orb = orbModel.ToMutable();
        Task task = OrbCmd.Channel(new ThrowingPlayerChoiceContext(), orb, issuingPlayer);
        return new CmdResult(task, success: true, "Added orb " + orbModel.Id.Entry);
    }

    public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
    {
        if (args.Length <= 1)
        {
            List<string> candidates = [.. ModelDb.Orbs.Select(o => o.Id.Entry)];
            return CompleteArgument(candidates, [], args.FirstOrDefault() ?? "");
        }
        return new CompletionResult
        {
            Type = CompletionType.Argument,
            ArgumentContext = CmdName
        };
    }
}
