namespace Launcher.App.Services;

/// <summary>启动随机小提示（彩蛋）：每次启动随机一条 Toast，可关（设置 → 关于 → 启动时随机小提示）。</summary>
public static class StartupTips
{
    private static readonly string[] Tips =
    [
        "多按几次启动按钮不会让世界加载更快，真的。",
        "镜像加速：把官方服务器当备胎的勇气可嘉。",
        "红石生电？听起来像在 Minecraft 里搞核聚变。",
        "今天也在为「再玩五分钟」买单。",
        "床在加载区外，记得重新设置重生点。",
        "下界合金不怕岩浆，但你会。",
        "你下载库文件的时间，够苦力怕重生 300 次。",
        "听说连点 10 次版本号会有好事，我瞎编的。",
        "TNT 不炸的方块只有基岩，和你的肝。",
        "性能档位调到极致，帧数靠意念起飞。",
        "官方源慢？切「镜像优先」，BMCLAPI 跑得飞快。",
        "你的存档已经 47GB 了，它不会自己变小。",
        "关服前记得看看掉落物，防战利品焦虑。",
        "末影龙的真正敌人是铁傀儡，不是你。",
        "这行字占用你 3 秒，够苦力怕点燃一次。",
        "版本隔离开着，存档才不会被模组带偏。",
    ];

    public static string Random() => Tips[System.Random.Shared.Next(Tips.Length)];
}
