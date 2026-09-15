using Godot;
using static DiceGame.UI.UiKit;

namespace DiceGame.UI;

public partial class CampaignUi
{
    private void BuildDiceSkill()
    {
        var sim=App.Sim!; var q=sim.CurrentSkillChoice;
        if(q is null) {App.Scene=App.DecisionScene;Invalidate();return;}
        var die=sim.State.Board.Single(d=>d?.Id==q.DieId)!;
        var definition=App.Data.Types[q.DiceType];
        _title.Text=$"{definition.Name} · {q.ResultPips} 点 · "+(q.Tier==3?(q.ResultPips==6?"重选三级分支 A / B":"选择三级分支 A / B"):"选择六级分支 C / D");
        Label(_content,"最终骰子种类已经确定，不会因选择或读档重新随机。技能只作用于这颗骰子；战斗、弹丸和装填计时均已暂停。",20,Muted);
        Label(_content, DiceGame.Core.DiceContent.RarityName(definition.Rarity),20,DiceGame.Core.DiceContent.RarityColor(definition.Rarity));
        var header=Row(_content); Dice(header,_root.Art,q.DiceType,q.ResultPips,126);
        var summary=Column(header);
        Label(summary,$"本次结果：{definition.Name} {q.ResultPips} 点 · 骰子编号 {q.DieId}",25,Mint);
        Label(summary,q.ResultPips==6?(q.Tier==3?"步骤 1 / 2：为最终种类重新选择 A 或 B。":"步骤 2 / 2：保留刚选的 "+die.Tier3+"，再选择 C 或 D。"):
            (q.FinishMerge ? "确认后合成齐射才会发出，并使用刚选择的技能。" : "确认后继续战斗。本次为进化或存档补选，不额外发射合成齐射。"),21,Gold);
        var grid=Add(Scroll(_content),new GridContainer{Columns=2,SizeFlagsHorizontal=SizeFlags.ExpandFill});
        foreach(var option in sim.SkillOptions)
        {
            string key=option.Key; long token=q.ChoiceId;
            var box=Card(grid,key+" · "+option.Name,definition.Name+" · 仅此骰子");box.CustomMinimumSize=new Vector2(520,320);
            Label(box,option.Description,25);
            var preview=sim.PreviewSkill(token,key);
            Label(box,$"选择后：齐射伤害 {preview.Volley:0.##} · 每轮 {preview.Count} 弹 · 装填 {preview.Reload:0.00} 秒",21,Mint);
            Label(box,"齐射伤害仅表示直接弹丸总量，不包含范围、连锁或条件增伤。",17,Muted);
            Button(box,"选择 "+key+" · "+option.Name,()=>Act($"diceSkill:{token}:{key}"));
        }
        Button(_content,"保存选择进度并返回城镇",()=>Act("town"));
    }
}
