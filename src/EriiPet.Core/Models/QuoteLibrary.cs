using System.Text.Json;

namespace EriiPet.Core.Models;

/// <summary>
/// 台词库 — 绘梨衣的便签短句。
/// 内置默认池，可被 assets/quotes.json 覆盖（同名 key 整组替换），方便玩家自定义台词包。
/// key 约定：
///   状态类  idle/happy/surprised/blush/sleepy/cuddle/reject/smirk/acknowledge/annoyed/wandering
///   交互类  power(双击) carry(牵手拖拽) drag_rage(甩动)
///   闲逛类  wander_spin/wander_bounce/wander_duckpet/wander_sit
///   仪式类  ritual_lunch birthday festival_* brother_song brother_rain
/// </summary>
public static class QuoteLibrary
{
    public static Dictionary<string, string[]> All { get; private set; } = BuiltIn();

    public static string[] Get(string key)
        => All.TryGetValue(key, out var v) ? v : [];

    /// <summary>随机取一句，空池返回 ""</summary>
    public static string Pick(string key)
    {
        var pool = Get(key);
        return pool.Length == 0 ? "" : pool[Random.Shared.Next(pool.Length)];
    }

    /// <summary>从 assets 目录加载 quotes.json（不存在或解析失败时用内置默认）</summary>
    public static void Load(string assetsDir)
    {
        try
        {
            var path = Path.Combine(assetsDir, "quotes.json");
            if (!File.Exists(path)) return;
            var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path));
            if (doc == null || doc.Count == 0) return;
            var merged = BuiltIn();
            foreach (var (k, el) in doc)
            {
                // 只接受字符串数组；忽略 _说明 等注释字段
                if (el.ValueKind != JsonValueKind.Array) continue;
                var arr = el.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Cast<string>().ToArray();
                if (arr.Length > 0) merged[k] = arr;
            }
            All = merged;
        }
        catch
        {
            // 台词文件损坏时静默回退内置默认
        }
    }

    private static Dictionary<string, string[]> BuiltIn() => new()
    {
        // ═══ 状态表情 ═══
        ["idle"] = [
            "世界很温柔。","Sakura…","风很轻。","今天也很好。","🌸",
            "哒…哒…","想吃棉花糖…","樱花…开了吗？","波板糖…甜甜的。",
        ],
        ["happy"] = [
            "你来了！","嘻嘻。","Sakura！","嗯！","开心。",
            "哒！哒！哒！","嘿嘿…哒哒！","Sakura，一起玩！","开心到想转圈圈！",
        ],
        ["surprised"] = ["啊。","……！","吓到了。","哒？！"],
        ["blush"]    = ["别看…","……///","害羞。","嗯…///"],
        ["sleepy"]   = ["晚安。","ZZZ…","困…","月亮真好看。","哒…Zzz…","梦里…游乐园见。"],
        ["cuddle"]   = ["贴贴。","好温暖。","喜欢。","牵着手…不放开。","贴贴…哒。","最喜欢Sakura了。"],
        ["reject"]   = ["不要。","放开…","不行。"],
        ["smirk"]    = ["嘿嘿。","秘密。","嘿嘿…哒。"],
        ["acknowledge"] = ["收到。","好的。","嗯。"],
        ["annoyed"]  = ["哼！","不理你了。","生气了！"],
        ["wandering"] = [
            "……","那边有什么呢…","走一走。","哼~哼~","🌸","今天的世界也很温柔。",
            "哒哒…散步。","游乐园…想再去一次。",
        ],

        // ═══ 交互 ═══
        ["power"] = ["接招吧！正义的铁拳！","哒哒哒哒——！","哒！嘿嘿…接招！"],
        ["carry"] = [
            "牵着走…去哪里呢？","Sakura带绘梨衣去玩！","哒哒哒…飞起来啦～",
            "手手…不放开哦。","牵着手…好安心。","转一圈…再转一圈！",
        ],
        ["drag_rage"] = ["嘎！…头晕晕。","太快了啦…哼！","呜…别甩了嘛。"],

        // ═══ 闲逛字幕 ═══
        ["wander_spin"]   = ["旋转木马…转啊转。","转圈圈…想和Sakura一起坐旋转木马。"],
        ["wander_bounce"] = ["哒！哒！","蹦蹦…哒！"],
        ["wander_duckpet"]= ["鸭子…嘿嘿。","软软的…小黄鸭。"],
        ["wander_sit"]    = ["天空好蓝…","坐在那里…看云。"],

        // ═══ 仪式 ═══
        ["ritual_lunch"] = ["12点了…该吃饭了。","中午…吃可乐饼吗？","可乐饼…想和Sakura一起吃。"],
        ["birthday"] = [
            "Sakura！今天是绘梨衣的生日…！","生日…有蛋糕吗？嘿嘿。",
            "Sakura记得绘梨衣的生日…好开心。","今天…是绘梨衣和Sakura的节日。",
        ],
        ["user_birthday"] = [
            "Sakura的生日…！生日快乐！！","今天是Sakura出生的日子…好重要。",
            "生日快乐，Sakura。绘梨衣的心意…收下吗？",
        ],
        ["festival_newyear"]    = ["新年…第一缕风。今年也和Sakura一起。"],
        ["festival_valentine"]  = ["情人节…这颗糖，给Sakura。"],
        ["festival_children"]   = ["今天是孩子的节日…绘梨衣要棒棒糖！"],
        ["festival_christmas_eve"] = ["平安夜…袜子要挂在床头。"],
        ["festival_christmas"]  = ["圣诞节！圣诞老人…会送小黄鸭来吗？"],
        ["festival_spring"]     = ["新年快乐…Sakura。一起看烟花吧。"],
        ["festival_qixi"]       = ["星星的节日…Sakura会陪绘梨衣吗？"],
        ["brother_song"] = ["哥哥的歌…在很远的地方。","哥哥的歌…Sakura想听吗？"],
        ["brother_rain"] = ["雨声…像哥哥的歌。"],

        // ═══ 专注陪伴 & 久坐关怀 ═══
        ["focus_start"] = [
            "要开始专注了吗…绘梨衣会安静陪着你。",
            "去吧去吧…绘梨衣在这里等你。哒。",
            "专注的时候…绘梨衣不来打扰哦。",
        ],
        ["focus_half"] = [
            "已经过半了…Sakura好厉害，继续加油。",
            "还在专注吗…喝口水再战。",
        ],
        ["focus_done"] = [
            "专注完成！Sakura好棒…哒哒！",
            "辛苦啦…休息一下吧。绘梨衣一直看着呢。",
            "完成了…！绘梨衣就说做得到吧。",
        ],
        ["sedentary"] = [
            "坐了很久了…要起来动一动吗？",
            "Sakura…去喝口水，走两步再回来。",
            "不动的话…会变成小魔王的哦。嘿嘿。",
        ],

        // ═══ 投喂（{0}=食物名） ═══
        ["feed"] = [
            "给绘梨衣的{0}…？开心！","{0}…！Sakura最好了。","咬一口{0}…哒。幸福。",
            "和{0}贴贴…然后吃掉。","嘿嘿…{0}是绘梨衣的了。",
        ],

        // ═══ 摸头 ═══
        ["headpat"] = [
            "摸摸头…乖。","嘿嘿…头发要乱了。","哒…最喜欢摸头了。",
            "呜…才、才不是小孩子…（头压低了些）","再摸一下…就一下。",
        ],
    };
}
