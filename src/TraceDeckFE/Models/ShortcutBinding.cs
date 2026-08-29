using TraceDeckFE.Localization;
using System.Windows.Input;

namespace TraceDeckFE.Models;

public enum ShortcutAction
{
    NewProject, OpenImage, OpenProject, Save, SaveAs, Undo, Redo, RedoAlternate, Fit, ActualSize,
    MoveLeft, MoveRight, MoveUp, MoveDown, MoveLeftFast, MoveRightFast, MoveUpFast, MoveDownFast, Cancel,
    ToggleVisible, ToggleLock, PickColor, OpacityDown, OpacityUp, ToggleGrid, ToggleCenters
}
public sealed record ShortcutBinding(ShortcutAction Action, Key Key, ModifierKeys Modifiers)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsGlobal => Action >= ShortcutAction.ToggleVisible;
    [System.Text.Json.Serialization.JsonIgnore]
    public string Gesture => (Modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl + " : "") +
        (Modifiers.HasFlag(ModifierKeys.Alt) ? "Alt + " : "") + (Modifiers.HasFlag(ModifierKeys.Shift) ? "Shift + " : "") +
        (Modifiers.HasFlag(ModifierKeys.Windows) ? "Win + " : "") + (Key switch { Key.OemOpenBrackets => "[", Key.OemCloseBrackets => "]", Key.D0 => "0", Key.D1 => "1", Key.Escape => "Esc", _ => Key.ToString() });
}
public static class ShortcutCatalog
{
    public static List<ShortcutBinding> Defaults()
    {
        const ModifierKeys c = ModifierKeys.Control, s = ModifierKeys.Shift, g = ModifierKeys.Control | ModifierKeys.Alt;
        return [new(ShortcutAction.NewProject, Key.N,c), new(ShortcutAction.OpenImage,Key.O,c), new(ShortcutAction.OpenProject,Key.O,c|s),
            new(ShortcutAction.Save,Key.S,c),new(ShortcutAction.SaveAs,Key.S,c|s),new(ShortcutAction.Undo,Key.Z,c),new(ShortcutAction.Redo,Key.Y,c),
            new(ShortcutAction.RedoAlternate,Key.Z,c|s),new(ShortcutAction.Fit,Key.D0,c),new(ShortcutAction.ActualSize,Key.D1,c),
            new(ShortcutAction.MoveLeft,Key.Left,0),new(ShortcutAction.MoveRight,Key.Right,0),new(ShortcutAction.MoveUp,Key.Up,0),new(ShortcutAction.MoveDown,Key.Down,0),
            new(ShortcutAction.MoveLeftFast,Key.Left,s),new(ShortcutAction.MoveRightFast,Key.Right,s),new(ShortcutAction.MoveUpFast,Key.Up,s),new(ShortcutAction.MoveDownFast,Key.Down,s),
            new(ShortcutAction.Cancel,Key.Escape,0),new(ShortcutAction.ToggleVisible,Key.V,g),new(ShortcutAction.ToggleLock,Key.L,g),new(ShortcutAction.PickColor,Key.I,g),
            new(ShortcutAction.OpacityDown,Key.OemOpenBrackets,g),new(ShortcutAction.OpacityUp,Key.OemCloseBrackets,g),new(ShortcutAction.ToggleGrid,Key.G,g),new(ShortcutAction.ToggleCenters,Key.C,g)];
    }
    public static string? Validate(ShortcutBinding binding, IEnumerable<ShortcutBinding> others)
    {
        if (!Enum.IsDefined(binding.Action) || !Enum.IsDefined(binding.Key) || binding.Key is Key.None or Key.System or Key.ImeProcessed or
            Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin ||
            ((int)binding.Modifiers & ~15) != 0) return L.Get("Shortcut.Invalid");
        if (binding.Modifiers.HasFlag(ModifierKeys.Windows) || binding.Modifiers == ModifierKeys.Alt && binding.Key is Key.Enter or Key.Z or Key.F4 ||
            binding.Key == Key.Delete && binding.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) || binding.Key == Key.F12)
            return L.Get("Shortcut.Reserved");
        if (binding.IsGlobal && (binding.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0)
            return L.Get("Shortcut.ModifierRequired");
        if (binding.Key == Key.V && binding.Modifiers == ModifierKeys.Control) return L.Get("Shortcut.PasteReserved");
        if (others.Any(b => b.Action != binding.Action && b.Key == binding.Key && b.Modifiers == binding.Modifiers)) return L.Get("Shortcut.Duplicate");
        return null;
    }
    public static List<ShortcutBinding> Sanitize(List<ShortcutBinding>? source)
    {
        var result = Defaults();
        if (source is null) return result;
        // Validate the final set, allowing intentional swaps without duplicate bindings.
        foreach (var b in source.Where(b => b is not null && Enum.IsDefined(b.Action)).GroupBy(b => b.Action).Select(g => g.Last()))
        {
            if (Validate(b, source.Where(other => other is not null)) is null) result[(int)b.Action] = b;
        }
        if (result.GroupBy(b => (b.Key,b.Modifiers)).Any(g => g.Count() > 1)) return Defaults();
        return result;
    }
}
