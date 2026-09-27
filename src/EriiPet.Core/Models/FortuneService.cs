namespace EriiPet.Core.Models;

/// <summary>
/// 每日一签 — 按日期确定性生成（同一天结果固定），绘梨衣风格的运势解读。
/// 无抽签存储：任何一天随时查看都是同一签。
/// </summary>
public static class FortuneService
{
    private static readonly (string Tier, string Text)[] Pool =
    [
        ("大吉", "今天说什么都会顺利…哒。去把想说的话说出口吧。"),
        ("大吉", "会遇到好吃的东西。绘梨衣的直觉不会错。"),
        ("吉", "风很轻。适合把一直没说的话说出口。"),
        ("吉", "下午会有小小的好事发生…比如捡到一颗糖。"),
        ("中吉", "普普通通的一天。普通就是最好的日子。"),
        ("中吉", "有点想睡…但没关系，世界还是很温柔。"),
        ("小吉", "可能会遇到一点小麻烦…绘梨衣陪着你。"),
        ("小吉", "慢一点也没关系。哒。"),
        ("末吉", "下午三点之后，运气会慢慢好转哦。"),
        ("末吉", "今天适合听一首歌，然后什么也不做。"),
    ];

    /// <summary>获取指定日期的签（同一日期永远同一签）</summary>
    public static (string Tier, string Text) GetDaily(DateTime date)
    {
        var seed = date.Year * 10000 + date.Month * 100 + date.Day;
        var idx = Math.Abs(seed * 2654435761) % Pool.Length; // 黄金比例散列，避免相邻日期总在同一档
        return Pool[idx];
    }
}
