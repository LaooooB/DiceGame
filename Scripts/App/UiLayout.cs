namespace DiceGame.App;

/// <summary>Input hit regions are independent of when Godot redraws a CanvasItem.</summary>
public static class UiLayout
{
    public static List<UiHit> Buttons(GameApp app)
    {
        List<UiHit> hits=[];
        void Add(string id,double x,double y,double w,double h,bool disabled=false)=>hits.Add(new(id,x,y,w,h,disabled));
        switch(app.Scene)
        {
            case "menu":
                Add("sound",369,64,34,34);Add("editDeck",331,581,69,29);
                if(app.ResumeData is not null) {Add("resume",32,715,368,55);Add("new",32,785,236,39);Add("help",280,785,120,39);}
                else {Add("start",32,723,368,59);Add("help",145,805,142,30);}break;
            case "play":
                Add("sound",327,20,35,35);Add("pause",373,20,35,35);
                Add("summon",28,807,270,42,app.Sim is not null && (app.Sim.State.Energy<app.Data.Game.Rules.SummonCost || app.Sim.Count==8));
                Add("help",310,807,94,42);break;
            case "paused":
                Add("continue",57,345,318,51);Add("sound",57,413,152,45);Add("music",223,413,152,45);
                Add("motion",57,474,318,43);Add("home",57,546,318,46);break;
            case "upgrade":
                if(app.Sim is not null) for(int i=0;i<app.Sim.State.Offers.Count;i++)Add("upgrade:"+app.Sim.State.Offers[i],27,278+i*127,378,111);break;
            case "over":Add("restart",54,508,324,54);Add("home",54,580,324,45);break;
            case "die":
                Add("closeDie",354,154,33,33);Add("closeDie",54,585,324,48);Add("recycle:"+app.SelectedSlot,54,651,324,43);break;
            case "deck":
                Add("deckBack",26,30,40,38);
                for(int i=0;i<app.Data.Dice.Length;i++)Add("deck:"+app.Data.Dice[i].Id,26+i%2*194,162+i/2*192,182,177);
                Add("deckSave",27,795,378,51,app.EditingDeck.Count==0);break;
            case "help":Add("closeHelp",355,116,33,33);Add("closeHelp",51,698,330,48);break;
            case "confirmNew":Add("confirmStart",57,415,318,47);Add("cancelNew",57,481,318,42);break;
        }
        return hits;
    }
}
