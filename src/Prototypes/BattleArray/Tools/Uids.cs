// 排兵布阵 造型源的 uid 表 —— 由生成器持有，不留给引擎「编辑器存盘时顺手补一个」：
// 那次存盘会同时给每个节点注入 unique_id、并省掉头行的 load_steps，把生成物变成一大片
// 纯格式化 diff（无限领主实测）。字符集与 13 位长度沿用引擎惯例（仓库既有 uid 只用 0-9 与 a-z），
// 已与本仓库全部既有 uid 比对无冲突。
// 注意：改这里的字符串 = 换资源身份，按 uid 引用它的地方会失联（当前都按 path 引用）。
//
// 八张兵种卡一次登记完（design.md 2.2 节），M2/M3 建模时不再动这张表 —— 免得每加一个模型
// 就多改一次本文件，把「登记表」变成「施工中的草稿」。

#nullable enable
using System.Collections.Generic;

namespace DemoFactory.Tools.BattleArray;

public static class Uids
{
    public static readonly IReadOnlyDictionary<string, string> Uid = new Dictionary<string, string>
    {
        // 人族（M2）
        ["HumanTank"] = "dnu4tkq9v7l2c",
        ["HumanWarrior"] = "eap6rhm0x4nk1",
        ["HumanArcher"] = "fqb3lvn7u5td2",
        ["HumanRider"] = "gr8c4mxe1pw0k",
        // 兽人（M3）
        ["OrcTank"] = "hta7zqu3nd6vm",
        ["OrcWarrior"] = "iuv2k5bph9r4l",
        ["OrcArcher"] = "jxy8nre4qc3mh",
        ["OrcRider"] = "kmb6qwt1zhe5d",
        // 预览舞台（GenPreview.cs）
        ["PreviewUnits"] = "lpd3fvn8k2u6b",
        ["PreviewModels"] = "mrg9cxh5t1qwe",
    };
}
