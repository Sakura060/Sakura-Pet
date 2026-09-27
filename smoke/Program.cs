// ═══════════════════════════════════════════════
//  绘梨衣桌宠 · 固化冒烟测试（v3.1~v3.4 全部用例整合）
//  运行：双击 运行冒烟测试.bat 或 dotnet run
//  说明：QuoteLibrary 用仓库 assets 真实文件；
//        AffectionService 用临时目录（绝不碰真实好感数据）。
// ═══════════════════════════════════════════════
using EriiPet.Core.Models;

var repoAssets = FindAssetsDir();
var isolatedData = Path.Combine(Path.GetTempPath(), "erii-smoke-data",
    DateTime.Now.Ticks.ToString());
Directory.CreateDirectory(isolatedData);

int pass = 0, fail = 0;
void Check(bool ok, string name)
{
    if (ok) { pass++; Console.WriteLine($"PASS  {name}"); }
    else { fail++; Console.WriteLine($"FAIL  {name}"); }
}

// ─── 1. 台词库（v3.1 标志梗 + v3.3/v3.4 扩充） ───
QuoteLibrary.Load(repoAssets);
Check(QuoteLibrary.Get("carry").Length >= 5, "牵手台词池（拖拽=牵手）");
Check(QuoteLibrary.Get("power").Any(q => q.Contains("正义的铁拳")), "双击 → 正义的铁拳");
Check(QuoteLibrary.Get("idle").Any(q => q.Contains("哒")), "哒口癖（idle）");
Check(QuoteLibrary.Get("happy").Any(q => q.Contains("哒")), "哒口癖（happy）");
Check(QuoteLibrary.Get("ritual_lunch").Any(q => q.Contains("可乐饼")), "午餐 → 可乐饼");
Check(QuoteLibrary.Get("wander_spin").Any(q => q.Contains("旋转木马")), "转圈 → 旋转木马");
Check(QuoteLibrary.Get("headpat").Length >= 4, "摸头台词池");
Check(QuoteLibrary.Get("focus_done").Length >= 3, "专注完成台词池");
Check(QuoteLibrary.Get("sedentary").Length >= 3, "久坐关怀台词池");
Check(QuoteLibrary.Get("user_birthday").Any(q => q.Contains("生日快乐")), "用户生日台词（quotes.json 覆盖生效）");
Check(QuoteLibrary.Get("feed").All(q => q.Contains("{0}")), "投喂台词含食物占位");
Check(string.Format(QuoteLibrary.Pick("feed"), "可乐饼").Contains("可乐饼"), "投喂台词格式化");
Check(QuoteLibrary.Pick("__no_such__") == "", "缺失 key 返回空串（不抛异常）");

// ─── 2. AI 对话状态注入（v3.4） ───
Check(ChatService.ComposeSystemPrompt(null) == ChatService.SystemPrompt, "无状态 → 原版人设提示词");
var composed = ChatService.ComposeSystemPrompt("现在是深夜。你们认识10天。");
Check(composed.StartsWith(ChatService.SystemPrompt) && composed.Contains("【此刻的真实情况】")
      && composed.Contains("现在是深夜"), "状态注入拼接正确");

// ─── 3. 仪式系统（生日/节日/纪念日） ───
var erii = TriggerAt(Fresh(), new DateTime(2026, 7, 11, 10, 5, 0));
Check(erii?.Type == RitualType.Birthday && erii.SpawnPetals && erii.PlayVoice,
      "7/11 → 绘梨衣生日（樱花雨+原声）");

var user = TriggerAt(Fresh(ub: "03-21"), new DateTime(2026, 3, 21, 10, 5, 0));
Check(user?.Type == RitualType.UserBirthday && user.SpawnPetals, "用户生日触发");
Check(TriggerAt(Fresh(ub: "abc"), new DateTime(2026, 3, 21, 10, 5, 0))?.Type != RitualType.UserBirthday,
      "非法生日格式不触发");
Check(TriggerAt(Fresh(ub: "03-21"), new DateTime(2026, 9, 26, 10, 5, 0))?.Type != RitualType.UserBirthday,
      "非生日当天不触发");

var spring = TriggerAt(Fresh(), new DateTime(2027, 2, 6, 10, 5, 0));
Check(spring?.Type == RitualType.Festival && spring.Message.Contains("新年快乐"), "春节（农历查表）");
var xmas = TriggerAt(Fresh(), new DateTime(2026, 12, 25, 10, 5, 0));
Check(xmas?.Type == RitualType.Festival && xmas.Message.Contains("圣诞"), "圣诞节");
var qixi = TriggerAt(Fresh(), new DateTime(2027, 8, 8, 10, 5, 0));
Check(qixi?.Type == RitualType.Festival, "七夕（农历查表）");

// ─── 4. 好感度（隔离数据目录） ───
var aff = new AffectionService(Path.Combine(isolatedData, "affection.json"));
var before = aff.Data.Score;
aff.RecordInteraction("feed");
Check(Math.Abs(aff.Data.Score - before - 0.2f) < 0.001f, "投喂 +0.2 好感");
aff.RecordInteraction("drag");
Check(Math.Abs(aff.Data.Score - before - 0.25f) < 0.001f, "牵手 +0.05 好感（不再扣分）");
aff.RecordInteraction("drag_rage");
Check(Math.Abs(aff.Data.Score - before - -0.25f) < 0.001f, "甩晕 -0.5 好感");

// ─── 5. 专注陪伴（v3.3） ───
var focus = new FocusService();
Check(!focus.IsActive && focus.Tick() == null, "专注初始空闲");
Check(focus.Start(30) != null, "专注开场白");
Check(focus.IsActive && focus.Tick() == null && !focus.JustFinished, "进行中安静");
Check(focus.CheckSedentary(DateTime.Now.AddMinutes(-50), 45) == null, "专注期间不提醒久坐");

var finish = typeof(FocusService).GetField("_endsAt",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
finish?.SetValue(focus, DateTime.Now.AddSeconds(-1));
var done = focus.Tick();
Check(done != null && focus.JustFinished, "专注到期 → 完成夸奖一次");
Check(focus.Tick() == null, "完成夸奖不重复");

// ─── 6. 久坐关怀（独立实例，不在专注中） ───
var sed = new FocusService();
Check(sed.CheckSedentary(DateTime.Now.AddHours(-2), 0) == null, "久坐阈值 0 = 关闭");
Check(sed.CheckSedentary(DateTime.Now.AddMinutes(-50), 45) != null, "久坐超阈值 → 提醒一次");
Check(sed.CheckSedentary(DateTime.Now.AddMinutes(-50), 45) == null, "久坐提醒不轰炸");
Check(sed.CheckSedentary(DateTime.Now, 45) == null, "交互后重置");

// ─── 7. 成就系统（v3.5） ───
var low = new AffectionService(Path.Combine(isolatedData, "low.json"));
var lowList = AchievementService.Evaluate(low);
Check(lowList.Count == 9, "成就共 9 项");
Check(lowList.First(a => a.Name == "初次相遇").Unlocked, "「初次相遇」常驻解锁（引导徽章）");
Check(!lowList.Any(a => a.Name == "羁绊" && a.Unlocked), "低好感不解锁羁绊");
var high = new AffectionService(Path.Combine(isolatedData, "high.json"));
high.Data.Score = 85;
high.Data.DaysSinceInstall = 100;
high.Data.TotalInteractions = 1200;
var highList = AchievementService.Evaluate(high);
Check(highList.Count(a => a.Unlocked) == 8, "高数据解锁 8/9 项（仅差一周年）");
Check(AchievementService.Format(highList).Contains("✅ 羁绊"), "成就格式化含 ✅");

// ─── 8. 每日一签（v3.5） ───
var f1 = FortuneService.GetDaily(new DateTime(2026, 9, 26));
var f1b = FortuneService.GetDaily(new DateTime(2026, 9, 26));
Check(f1 == f1b, "同一天同一签");
var f2x = FortuneService.GetDaily(new DateTime(2026, 9, 27));
var pool = typeof(FortuneService).GetField("Pool",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
Check(f1.Tier is "大吉" or "吉" or "中吉" or "小吉" or "末吉", "签文档位合法");
Check(!string.IsNullOrWhiteSpace(f1.Text) && f1.Text.Length > 5, "签文有解读");

// ─── 9. 成就播报（v3.6 NewlyUnlocked 差分） ───
var known = new HashSet<string>();
var ann = new AffectionService(Path.Combine(isolatedData, "ann.json"));
var firstAnn = AchievementService.NewlyUnlocked(ann, known);
Check(firstAnn.Any(a => a.Name == "初次相遇"), "首次差分登记基线（含常驻徽章）");
Check(AchievementService.NewlyUnlocked(ann, known).Count == 0, "基线后无重复播报");
ann.Data.Score = 85;
var after = AchievementService.NewlyUnlocked(ann, known);
Check(after.Count == 2 && after.All(a => a.Name is "朋友以上" or "羁绊"), "好感跨档 → 播报两个新成就");

// ─── 10. 每周自动备份（v3.8，隔离数据目录） ───
DataPaths.SetBaseDir(isolatedData);
File.WriteAllText(Path.Combine(isolatedData, "affection.json"), "{}");
AutoBackupService.RunIfDue();
var backupDir = Path.Combine(isolatedData, "backups");
var zips = Directory.GetFiles(backupDir, "auto_*.zip");
Check(zips.Length == 1, "首次运行 → 自动备份 1 份");
Check(new System.IO.Compression.ZipArchive(
    System.IO.File.OpenRead(zips[0]), System.IO.Compression.ZipArchiveMode.Read).Entries.Count >= 1,
    "备份包含数据文件");
AutoBackupService.RunIfDue();
Check(Directory.GetFiles(backupDir, "auto_*.zip").Length == 1, "7 天内不重复备份");
DataPaths.SetBaseDir(null); // 还原默认路径

// ─── 11. 语音模型默认值（v4.0，交接文档） ───
Check(!string.IsNullOrWhiteSpace(VoiceModelService.DefaultRefAudio)
      && VoiceModelService.DefaultRefAudio.EndsWith("ref_erii.wav"), "默认参考音频路径");
Check(VoiceModelService.DefaultPromptText.Contains("出门冒险"), "默认参考音频原文");

Console.WriteLine($"\n═════ {pass} passed, {fail} failed ═════");
Cleanup(isolatedData);
return fail == 0 ? 0 : 1;

// ─── helpers ───
static string FindAssetsDir()
{
    var dir = AppDomain.CurrentDomain.BaseDirectory;
    for (int i = 0; i < 8; i++)
    {
        var test = Path.Combine(dir, "assets");
        if (Directory.Exists(test) && File.Exists(Path.Combine(test, "quotes.json")))
            return test;
        var parent = Path.GetDirectoryName(dir);
        if (parent == null || parent == dir) break;
        dir = parent;
    }
    throw new InvalidOperationException("找不到 assets（含 quotes.json）");
}

// 第一次 Check 被"今日首次互动"占用，隔 6 分钟再 Check 才轮到生日/节日
static RitualEvent? TriggerAt(RitualService rs, DateTime t)
{
    rs.Check(t);
    return rs.Check(t.AddMinutes(6));
}

// AffectionService 用临时目录（v3.4 起支持注入），完全不碰真实数据
static RitualService Fresh(string? ub = null)
{
    var aff = new AffectionService(Path.Combine(
        Path.GetTempPath(), "erii-smoke-data", Guid.NewGuid().ToString("N"), "affection.json"));
    var rs = new RitualService(aff, new RelationshipService());
    if (ub != null) rs.UserBirthday = ub;
    return rs;
}

static void Cleanup(string dir)
{
    try { Directory.Delete(dir, recursive: true); } catch { }
}
