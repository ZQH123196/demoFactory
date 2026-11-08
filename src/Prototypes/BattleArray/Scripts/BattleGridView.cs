#nullable enable
using System.Collections.Generic;
using DemoFactory.GugaFramework.I18n;
using DemoFactory.Prototypes.BattleArray.Logic;
using Godot;
// 本文件的方格用 Godot 的 Vector2/Vector3，格子归属哪一军用逻辑层的 Side —— 两边同名，起别名（CS0104）。
using BattleSide = DemoFactory.Prototypes.BattleArray.Logic.Side;

namespace DemoFactory.Prototypes.BattleArray;

/// <summary>
/// 布阵网格 —— 把 5.1 节那张「每方 5 列 × 3 行 = 15 格」铺在地上，让摆位这件事看得见。
///
/// 它是<b>只读表现</b>：格子该是什么颜色，全部现问 <see cref="DeploymentBoard"/> 与拖拽中的合法性，
/// 本节点不存「哪格站了谁」（那是棋盘的账）。
/// 每格左上角那行 <c>排n列m</c> 是给调试与出图看的：摆子直达口报的坐标能不能在地上对上，
/// 看一眼截图就知道 —— 没有这行字，要核对「放到指定格」放没放对，只能靠读日志考古。
///
/// 架构位置：Scripts —— 表现层节点（吃 Godot 类型，不进 dotnet test）。
/// 地上的方片是 UI 几何，不是兵的造型（兵的造型在 <c>Models/*.glb</c>，见 docs/模型与动画准则.md）。
/// </summary>
public partial class BattleGridView : Node3D
{
    /// <summary>空格：比地面亮一档，看得出「这里能站人」。</summary>
    private static readonly Color EmptyCell = new(0.26f, 0.31f, 0.26f);

    /// <summary>已占格：再亮一档，一眼数得出场上摆了几个。</summary>
    private static readonly Color FilledCell = new(0.34f, 0.42f, 0.34f);

    /// <summary>拖拽中／点中的那一格（合法）。</summary>
    private static readonly Color LegalCell = new(0.30f, 0.72f, 0.38f);

    /// <summary>拖拽中落在非法位置（已占、接敌带、场外）。</summary>
    private static readonly Color IllegalCell = new(0.72f, 0.24f, 0.22f);

    /// <summary>敌方那 15 格：只当阵地参照，摆不了（本原型的布阵期只能摆我方）。</summary>
    private static readonly Color EnemyCell = new(0.33f, 0.26f, 0.24f);

    /// <summary>选中兵所在格。</summary>
    private static readonly Color SelectedCell = new(0.95f, 0.80f, 0.30f);

    /// <summary>格面离地高度（米）：抬一点，别和 400 米那块地面糊出 z-fighting。</summary>
    private const float PlateY = 0.02f;

    private readonly MeshInstance3D[,] _plates =
        new MeshInstance3D[BattleContract.Rows, BattleContract.Columns];
    private readonly StandardMaterial3D[,] _plateMaterials =
        new StandardMaterial3D[BattleContract.Rows, BattleContract.Columns];

    /// <summary>拖拽／点选中的那一格（null = 没有）；<see cref="Legal"/> 决定涂绿还是涂红。</summary>
    private (int Row, int Column)? _marked;
    private bool _legal = true;
    private (int Row, int Column)? _selected;

    /// <summary>铺出 15 格与格号（一次性；之后只改颜色）。</summary>
    public void Build()
    {
        for (var row = 0; row < BattleContract.Rows; row++)
        {
            for (var column = 0; column < BattleContract.Columns; column++)
            {
                var center = BattleContract.CellCenter(BattleSide.Player, row, column);
                var material = Unshaded(EmptyCell);
                // PlaneMesh 的默认朝向就是水平朝上（M5 那块 400 米地面没写 orientation、出图看得见 ⇒ 同一档）
                var plate = new MeshInstance3D
                {
                    Name = $"Cell{row}{column}",
                    Mesh = new PlaneMesh
                    {
                        Size = new Vector2(BattleContract.CellSize - 0.12f, BattleContract.CellSize - 0.12f),
                    },
                    MaterialOverride = material,
                    Position = new Vector3(center.X, PlateY, center.Y),
                };
                AddChild(plate);
                _plates[row, column] = plate;
                _plateMaterials[row, column] = material;

                // 格号写在格子左上角（出图与肉眼都读得到，摆子直达口的坐标才有地方对照）。
                // 朝向交给 billboard 管（写了 Rotation 也会被它覆盖，别留这种假旋钮）。
                var tag = new Label3D
                {
                    Text = Loc.Fmt("排{0}列{1}", row + 1, column + 1),
                    FontSize = 28,
                    Position = new Vector3(center.X - BattleContract.CellSize * 0.5f + 0.12f,
                        PlateY + 0.02f, center.Y - BattleContract.CellSize * 0.5f + 0.12f),
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    Modulate = new Color(1f, 1f, 1f, 0.78f),
                    NoDepthTest = true,
                };
                AddChild(tag);
            }
        }

        // 敌方阵地同样 15 格，涂成对照色并摆过去 —— 让人一眼看出「对面也是这么站的」
        foreach (var placement in Formations.ShieldWall.Placements)
        {
            var center = BattleContract.CellCenter(BattleSide.Enemy, placement.Row, placement.Column);
            var ghost = new MeshInstance3D
            {
                Name = $"EnemyCell{placement.Row}{placement.Column}",
                Mesh = new PlaneMesh
                {
                    Size = new Vector2(BattleContract.CellSize - 0.24f, BattleContract.CellSize - 0.24f),
                },
                MaterialOverride = Unshaded(EnemyCell),
                Position = new Vector3(center.X, PlateY, center.Y),
            };
            AddChild(ghost);
        }
    }

    /// <summary>棋盘一变就重刷格子底色（空格/已占两档）。</summary>
    public void Refresh(DeploymentBoard board)
    {
        for (var row = 0; row < BattleContract.Rows; row++)
        {
            for (var column = 0; column < BattleContract.Columns; column++)
            {
                var filled = board.At(row, column) is not null;
                _plateMaterials[row, column].AlbedoColor = filled ? FilledCell : EmptyCell;
            }
        }
        RepaintMarks();
    }

    /// <summary>
    /// 拖拽／点选中的那一格。row/column 为 -1（或落在布阵区外）时清掉标记。
    /// 合法性由调用方判（<see cref="BattleContract.TryCellFromWorldPoint"/> ＋ 棋盘 At()），这里只负责画。
    /// </summary>
    public void MarkCell(int row, int column, bool legal)
    {
        if (row < 0 || row >= BattleContract.Rows || column < 0 || column >= BattleContract.Columns)
        {
            ClearMark();
            return;
        }
        _marked = (row, column);
        _legal = legal;
        RepaintMarks();
    }

    public void ClearMark()
    {
        if (_marked is null) return;
        _marked = null;
        RepaintMarks();
    }

    /// <summary>暂停面板正在读的那个兵所在格（选中描边用，选没选中都画得出「在读谁」）。</summary>
    public void MarkSelected(int row, int column)
    {
        _selected = row >= 0 && column >= 0 ? (row, column) : null;
        RepaintMarks();
    }

    private void RepaintMarks()
    {
        if (_selected is { } sel && _marked is null)
        {
            _plateMaterials[sel.Row, sel.Column].AlbedoColor = SelectedCell;
        }
        if (_marked is not { } mark) return;
        _plateMaterials[mark.Row, mark.Column].AlbedoColor = _legal ? LegalCell : IllegalCell;
    }

    private static StandardMaterial3D Unshaded(Color color) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = color,
    };

    /// <summary>格子在屏幕上的可点范围由相机反投决定，这里只暴露「哪格对应哪个中心」给调试口核对。</summary>
    public static Vector3 WorldCenterOf(int row, int column)
    {
        var center = BattleContract.CellCenter(BattleSide.Player, row, column);
        return new Vector3(center.X, 0f, center.Y);
    }
}
