using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Nodes.Debug;

namespace BetterConsole.Patch;

[HarmonyPatch]
internal static class ConsoleTabPatch
{
    private const string PopupPanelName = "BetterConsoleTabPopupPanel";
    private const int MaxVisibleCandidates = 6;
    private const float PopupMargin = 8f;
    private const float PopupPadding = 18f;
    private const float PopupWidthBuffer = 96f;
    private const float PopupHeightBuffer = 42f;
    private const float MinPopupWidth = 560f;
    private const float MaxPopupWidth = 960f;

    private static readonly MethodInfo AcceptSelectionMethod = AccessTools.Method(typeof(NDevConsole), "AcceptSelection");
    private static readonly MethodInfo RenderSelectionMenuMethod = AccessTools.Method(typeof(NDevConsole), "RenderSelectionMenu");
    private static readonly MethodInfo UpdateGhostTextMethod = AccessTools.Method(typeof(NDevConsole), "UpdateGhostText");

    [ThreadStatic]
    private static bool _tabShouldAcceptSelection;

    [ThreadStatic]
    private static bool _enterShouldProcessCommand;

    [ThreadStatic]
    private static bool _textChangeWasProgrammatic;

    [ThreadStatic]
    private static bool _textChangeWasInSelectionMode;

    [ThreadStatic]
    private static bool _ghostTextWasInSelectionMode;

    [HarmonyPatch(typeof(NDevConsole), "_Ready")]
    [HarmonyPostfix]
    public static void ReadyPostfix(NDevConsole __instance, RichTextLabel ____tabBuffer)
    {
        var popupPanel = GetOrCreatePopupPanel(__instance);
        if (____tabBuffer.GetParent() != __instance)
        {
            ____tabBuffer.GetParent()?.RemoveChild(____tabBuffer);
            __instance.AddChild(____tabBuffer);
        }

        ____tabBuffer.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        ____tabBuffer.MouseFilter = Control.MouseFilterEnum.Ignore;
        ____tabBuffer.AutowrapMode = TextServer.AutowrapMode.Off;
        ____tabBuffer.ZIndex = 100;
        ____tabBuffer.Visible = false;
        popupPanel.Visible = false;
    }

    [HarmonyPatch(typeof(NDevConsole), "MakeHalfScreen")]
    [HarmonyPostfix]
    public static void MakeHalfScreenPostfix(NDevConsole __instance, TabCompletionState ____tabCompletion)
    {
        if (HasCandidates(____tabCompletion))
            RenderSelectionMenuMethod.Invoke(__instance, []);
    }

    [HarmonyPatch(typeof(NDevConsole), "MakeFullScreen")]
    [HarmonyPostfix]
    public static void MakeFullScreenPostfix(NDevConsole __instance, TabCompletionState ____tabCompletion)
    {
        if (HasCandidates(____tabCompletion))
            RenderSelectionMenuMethod.Invoke(__instance, []);
    }

    [HarmonyPatch(typeof(NDevConsole), "DisableTabBuffer")]
    [HarmonyPostfix]
    public static void DisableTabBufferPostfix(NDevConsole __instance)
    {
        HidePopupPanel(__instance);
    }

    [HarmonyPatch(typeof(NDevConsole), "HideConsole")]
    [HarmonyPostfix]
    public static void HideConsolePostfix(NDevConsole __instance, RichTextLabel ____tabBuffer, TabCompletionState ____tabCompletion)
    {
        ____tabCompletion.Reset();
        if (____tabBuffer != null)
        {
            ____tabBuffer.Text = string.Empty;
            ____tabBuffer.Visible = false;
        }

        HidePopupPanel(__instance);
    }

    [HarmonyPatch(typeof(NDevConsole), "_Input")]
    [HarmonyPrefix]
    public static bool InputPrefix(
        NDevConsole __instance,
        InputEvent inputEvent,
        TabCompletionState ____tabCompletion)
    {
        _tabShouldAcceptSelection = false;
        _enterShouldProcessCommand = false;

        if (inputEvent is not InputEventKey { Pressed: true } keyEvent || !__instance.Visible)
            return true;

        if (keyEvent.Keycode == Key.Tab && HasCandidates(____tabCompletion))
        {
            _tabShouldAcceptSelection = true;
            return true;
        }

        if (keyEvent.Keycode == Key.Enter && HasCandidates(____tabCompletion))
        {
            _enterShouldProcessCommand = true;
            ____tabCompletion.InSelectionMode = false;
        }

        return true;
    }

    [HarmonyPatch(typeof(NDevConsole), "_Input")]
    [HarmonyPostfix]
    public static void InputPostfix(NDevConsole __instance, RichTextLabel ____tabBuffer, TabCompletionState ____tabCompletion)
    {
        if (_enterShouldProcessCommand)
        {
            ____tabCompletion.Reset();
            ____tabBuffer.Text = string.Empty;
            DisableTabBuffer(__instance, ____tabBuffer);
        }

        _tabShouldAcceptSelection = false;
        _enterShouldProcessCommand = false;
    }

    [HarmonyPatch(typeof(NDevConsole), "NavigateSelection")]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    public static void NavigateSelectionPrefix(TabCompletionState ____tabCompletion, int direction)
    {
        if (!_tabShouldAcceptSelection || !HasCandidates(____tabCompletion))
            return;

        ____tabCompletion.SelectionIndex = (____tabCompletion.SelectionIndex - direction + ____tabCompletion.CompletionCandidates.Count) % ____tabCompletion.CompletionCandidates.Count;
    }

    [HarmonyPatch(typeof(NDevConsole), "NavigateSelection")]
    [HarmonyPostfix]
    public static void NavigateSelectionPostfix(NDevConsole __instance)
    {
        if (_tabShouldAcceptSelection)
            AcceptSelectionMethod.Invoke(__instance, []);
    }

    [HarmonyPatch(typeof(NDevConsole), "AutocompleteCommand")]
    [HarmonyPostfix]
    public static void AutocompleteCommandPostfix(NDevConsole __instance)
    {
        UpdateGhostTextMethod.Invoke(__instance, []);
    }

    [HarmonyPatch(typeof(NDevConsole), "OnInputTextChanged")]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    public static void OnInputTextChangedPrefix(TabCompletionState ____tabCompletion)
    {
        _textChangeWasProgrammatic = ____tabCompletion.ProgrammaticTextChange;
        _textChangeWasInSelectionMode = ____tabCompletion.InSelectionMode;
        if (!_textChangeWasProgrammatic)
            ____tabCompletion.InSelectionMode = false;
    }

    [HarmonyPatch(typeof(NDevConsole), "OnInputTextChanged")]
    [HarmonyPostfix]
    public static void OnInputTextChangedPostfix(
        NDevConsole __instance,
        string newText,
        DevConsole ____devConsole,
        RichTextLabel ____tabBuffer,
        TabCompletionState ____tabCompletion)
    {
        if (_textChangeWasProgrammatic)
            return;

        ____tabCompletion.InSelectionMode = _textChangeWasInSelectionMode;
        RefreshCandidates(__instance, ____devConsole, newText, ____tabBuffer, ____tabCompletion, includeBlank: false);
    }

    [HarmonyPatch(typeof(NDevConsole), "UpdateGhostText")]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    public static void UpdateGhostTextPrefix(TabCompletionState ____tabCompletion)
    {
        _ghostTextWasInSelectionMode = ____tabCompletion.InSelectionMode;
        ____tabCompletion.InSelectionMode = false;
    }

    [HarmonyPatch(typeof(NDevConsole), "UpdateGhostText")]
    [HarmonyPostfix]
    public static void UpdateGhostTextPostfix(TabCompletionState ____tabCompletion)
    {
        ____tabCompletion.InSelectionMode = _ghostTextWasInSelectionMode;
    }

    [HarmonyPatch(typeof(NDevConsole), "RenderSelectionMenu")]
    [HarmonyPostfix]
    public static void RenderSelectionMenuPostfix(
        NDevConsole __instance,
        LineEdit ____inputBuffer,
        RichTextLabel ____outputBuffer,
        RichTextLabel ____tabBuffer,
        TabCompletionState ____tabCompletion,
        string ____symbolPrompt,
        string ____symbolUp,
        string ____symbolDown)
    {
        if (!HasCandidates(____tabCompletion))
        {
            ____tabBuffer.Text = string.Empty;
            DisableTabBuffer(__instance, ____tabBuffer);
            return;
        }

        var lines = new List<string>
        {
            ____tabCompletion.LastCompletionResult?.Type switch
            {
                CompletionType.Command => "Command:",
                CompletionType.Subcommand => ____tabCompletion.LastCompletionResult.ArgumentContext + ":",
                CompletionType.Argument => ____tabCompletion.LastCompletionResult.ArgumentContext + " argument:",
                _ => "Completion:",
            },
            "[color=gray]" + ____symbolUp + "/" + ____symbolDown + ": select, Tab: complete[/color]",
            "",
        };

        var count = ____tabCompletion.CompletionCandidates.Count;
        if (count <= MaxVisibleCandidates)
        {
            for (var i = 0; i < count; i++)
                AddCandidate(lines, ____tabCompletion, ____symbolPrompt, i);
        }
        else
        {
            var start = Math.Max(0, ____tabCompletion.SelectionIndex - MaxVisibleCandidates / 2);
            var end = Math.Min(count, start + MaxVisibleCandidates);
            if (end - start < MaxVisibleCandidates)
                start = Math.Max(0, end - MaxVisibleCandidates);

            if (start > 0)
                lines.Add($"[color=gray]{____symbolUp} {start} more above {____symbolUp}[/color]");

            for (var i = start; i < end; i++)
                AddCandidate(lines, ____tabCompletion, ____symbolPrompt, i);

            if (end < count)
                lines.Add($"[color=gray]{____symbolDown} {count - end} more below {____symbolDown}[/color]");
        }

        lines.Add("");
        lines.Add($"[color=gray]({count} matches)[/color]");
        ____tabBuffer.Text = string.Join("\n", lines);
        ____outputBuffer.Visible = true;
        PositionTabBuffer(__instance, ____inputBuffer, GetOrCreatePopupPanel(__instance), ____tabBuffer, ____tabCompletion, lines);
        EnableTabBuffer(__instance, ____tabBuffer);
    }

    private static void RefreshCandidates(
        NDevConsole console,
        DevConsole devConsole,
        string text,
        RichTextLabel tabBuffer,
        TabCompletionState state,
        bool includeBlank)
    {
        if (!includeBlank && string.IsNullOrWhiteSpace(text))
        {
            state.Reset();
            tabBuffer.Text = string.Empty;
            DisableTabBuffer(console, tabBuffer);
            return;
        }

        var completion = devConsole.GetCompletionResults(text);
        if (string.IsNullOrWhiteSpace(text) && completion.Candidates.Count == 0)
            completion = devConsole.GetCompletionResults("");

        state.LastCompletionResult = completion;
        if (completion.Candidates.Count == 0)
        {
            state.Reset();
            tabBuffer.Text = string.Empty;
            DisableTabBuffer(console, tabBuffer);
            return;
        }

        var selected = state.SelectionIndex >= 0 && state.SelectionIndex < state.CompletionCandidates.Count
            ? state.CompletionCandidates[state.SelectionIndex]
            : null;
        var oldIndex = Math.Max(0, state.SelectionIndex);

        state.CompletionCandidates.Clear();
        state.CompletionCandidates.AddRange(completion.Candidates);
        state.InSelectionMode = true;

        var selectedIndex = selected == null ? -1 : state.CompletionCandidates.IndexOf(selected);
        state.SelectionIndex = selectedIndex >= 0 ? selectedIndex : Math.Min(oldIndex, state.CompletionCandidates.Count - 1);

        RenderSelectionMenuMethod.Invoke(console, []);
    }

    private static bool HasCandidates(TabCompletionState state) =>
        state.InSelectionMode && state.CompletionCandidates.Count > 0;

    private static void EnableTabBuffer(NDevConsole console, RichTextLabel tabBuffer)
    {
        GetOrCreatePopupPanel(console).Visible = true;
        tabBuffer.Visible = true;
    }

    private static void DisableTabBuffer(NDevConsole console, RichTextLabel tabBuffer)
    {
        HidePopupPanel(console);
        tabBuffer.Visible = false;
    }

    private static void PositionTabBuffer(NDevConsole console, LineEdit inputBuffer, Panel popupPanel, RichTextLabel tabBuffer, TabCompletionState state, List<string> lines)
    {
        var consoleRect = console.GetGlobalRect();
        var inputRect = inputBuffer.GetGlobalRect();
        var fontSize = Math.Max(12, inputBuffer.GetThemeFontSize("font_size"));
        var charWidth = fontSize * 0.66f;
        var lineHeight = fontSize + 7f;

        var commandPrefix = state.LastCompletionResult?.CommandPrefix ?? "";
        var prefixLength = Math.Min(commandPrefix.Length, inputBuffer.Text.Length);
        var x = inputRect.Position.X - consoleRect.Position.X + prefixLength * charWidth;
        var availableWidth = Math.Max(MinPopupWidth, console.Size.X - x - PopupMargin);
        var maxColumns = lines.Select(line => GetDisplayColumns(StripBbCode(line))).DefaultIfEmpty(0).Max();
        var contentWidth = Math.Min(MaxPopupWidth, Math.Max(MinPopupWidth, maxColumns * charWidth + PopupPadding * 2f + PopupWidthBuffer));
        var width = Math.Min(contentWidth, availableWidth);
        if (x + width > console.Size.X - PopupMargin)
            x = Math.Max(PopupMargin, console.Size.X - width - PopupMargin);

        var height = Math.Min(inputRect.Position.Y - consoleRect.Position.Y - PopupMargin, lines.Count * lineHeight + PopupPadding * 2f + PopupHeightBuffer);
        height = Math.Max(lineHeight * 4f, height);
        var y = Math.Max(PopupMargin, inputRect.Position.Y - consoleRect.Position.Y - height - 4f);

        popupPanel.Position = new Vector2(x, y);
        popupPanel.SetSize(new Vector2(width, height));
        tabBuffer.Position = new Vector2(x + PopupPadding, y + PopupPadding);
        tabBuffer.SetSize(new Vector2(Math.Max(1f, width - PopupPadding * 2f), Math.Max(1f, height - PopupPadding * 2f)));
    }

    private static Panel GetOrCreatePopupPanel(NDevConsole console)
    {
        var popupPanel = console.GetNodeOrNull<Panel>(PopupPanelName);
        if (popupPanel != null)
            return popupPanel;

        popupPanel = new Panel
        {
            Name = PopupPanelName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 99,
            Visible = false,
        };

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.025f, 0.025f, 0.04f, 0.76f),
            BorderColor = new Color(0f, 0.831f, 1f, 0.22f),
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(4);
        popupPanel.AddThemeStyleboxOverride("panel", style);

        console.AddChild(popupPanel);
        return popupPanel;
    }

    private static void HidePopupPanel(NDevConsole console)
    {
        var popupPanel = console.GetNodeOrNull<Panel>(PopupPanelName);
        if (popupPanel == null)
            return;

        popupPanel.Visible = false;
        popupPanel.SetSize(Vector2.Zero);
    }

    private static string StripBbCode(string text)
    {
        while (true)
        {
            var start = text.IndexOf('[');
            var end = start < 0 ? -1 : text.IndexOf(']', start);
            if (start < 0 || end < 0)
                return text;

            text = text.Remove(start, end - start + 1);
        }
    }

    private static int GetDisplayColumns(string text)
    {
        var columns = 0;
        foreach (var c in text)
            columns += c <= 0x7f ? 1 : 2;

        return columns;
    }

    private static void HideGhostText(Label ghostTextLabel)
    {
        ghostTextLabel.Visible = false;
        ghostTextLabel.Text = string.Empty;
    }

    private static void ShowGhostText(Label ghostTextLabel, string ghostText)
    {
        ghostTextLabel.Text = ghostText;
        ghostTextLabel.Visible = true;
    }

    private static void AddCandidate(List<string> lines, TabCompletionState state, string prompt, int index)
    {
        if (index < 0 || index >= state.CompletionCandidates.Count)
            return;

        var candidate = state.CompletionCandidates[index];
        lines.Add(index == state.SelectionIndex ? $"[color=yellow]{prompt} {candidate}[/color]" : "  " + candidate);
    }
}
