using DiceGame.Core;
using Godot;

namespace DiceGame.UI;

public partial class CampaignUi
{
    internal AcceptDialog ShowDiceCatalog(string id)
    {
        var die = App.Data.Types[id]; var set = App.Data.Skills[id];
        string text = DiceContent.RarityName(die.Rarity) + " · " + die.Tag + "\n" + die.Description + "\n\n";
        text += string.Join("\n\n", set.Level3.Concat(set.Level6).Select(s => (s.Key is "A" or "B" ? "3点 " : "6点 ") + s.Key + " · " + s.Name + "\n" + s.Description));
        var dialog = new AcceptDialog { Title = die.Name + " · 强化图鉴", DialogText = text, DialogAutowrap = true, OkButtonText = "关闭", Exclusive = true };
        AddChild(dialog);
        dialog.GetLabel().AddThemeFontOverride("font", _root.Art.Font(500));
        dialog.GetLabel().AddThemeFontSizeOverride("font_size", 20);
        void Close() { if (!dialog.IsQueuedForDeletion()) dialog.QueueFree(); }
        dialog.Confirmed += Close; dialog.Canceled += Close;
        dialog.PopupCentered(new Vector2I(940, 760));
        return dialog;
    }
}
