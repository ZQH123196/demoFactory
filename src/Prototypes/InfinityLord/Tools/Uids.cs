// 灰盒预制件的 uid 表 —— 由生成器持有，不留给引擎"编辑器存盘时顺手补一个"：
// 那次存盘会同时给每个节点注入 unique_id、并省掉头行的 load_steps，把生成物变成一大片
// 纯格式化 diff（实测）。字符集与 13 位长度沿用引擎惯例，已跟本仓库 188 个既有 uid 比对无冲突。
// 注意：改这里的字符串 = 换资源身份，按 uid 引用它的地方会失联（当前都按 path 引用，见 Main.cs）。
// （2026-09-23 自 uids.mjs 移植；Node 原版已删，史实见 git log）

#nullable enable
using System.Collections.Generic;

namespace DemoFactory.Tools.InfinityLord;

public static class Uids
{
    public static readonly IReadOnlyDictionary<string, string> Uid = new Dictionary<string, string>
    {
        // 单位
        ["Ranger"] = "uid://kb0ob5vbeb780",
        ["Warrior"] = "uid://tkwejbddqoo1j",
        ["Knight"] = "uid://gbp1tqa01luka",
        ["Goblin"] = "uid://mu3jlw0qjwo1e",
        ["GoblinElite"] = "uid://fg077paijmvme",
        ["GoblinBrute"] = "uid://e8gnvakto666t",
        ["BossGuard"] = "uid://jof0vff810hhm",
        ["Troll"] = "uid://u533ntk6ofmk0",
        // 建筑与道具
        ["Palace"] = "uid://6hkq17th5qh3a",
        ["GuildHall"] = "uid://315t666lili30",
        ["GuildWarrior"] = "uid://akd6hlw6nuwo0",
        ["Market"] = "uid://fbhdb5g3pa1mp",
        ["Tavern"] = "uid://hnvdwe8m75h0t",
        ["Apothecary"] = "uid://tpueiqocmoh1k",
        ["MonsterLair"] = "uid://jqogfcwvvpc6q",
        ["BossLair"] = "uid://7bvgg6ubgd5ng",
        ["BountyFlag"] = "uid://gc13d586ipev1",
        // 预览场景
        ["PreviewUnits"] = "uid://q73opek3ajmh7",
        ["PreviewBuildings"] = "uid://k6jvhglltv00d",
        ["PreviewModels"] = "uid://8r4wqm2xjdtne",
    };
}