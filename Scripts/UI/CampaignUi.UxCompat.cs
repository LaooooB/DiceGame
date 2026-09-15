global using DiceGame.Presentation;

namespace DiceGame.UI;

public partial class CampaignUi
{
    private const string Danger = "#FF8F86";

    internal void ReleaseLegacyDragAlias()
    {
        if (ReferenceEquals(_dragArt, _uxDragArt)) _dragArt = null;
    }

    internal bool UxCanSkipOrder => App.Scene == "play" && App.Sim?.CanSkipOrder == true;
    internal void UxSkipOrder() => Act("orderSkip");
}
