#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DemoFactory.GugaFramework.Abilities;
using Shouldly;
using Xunit;

namespace BattleArray.Tests;

/// <summary>
/// <b>事件形状的长期结构断言</b>（2026-09-29 形状迁移落地时挂上，常驻）。
/// 事件类型就是名单本身：这一份不比对任何一行文本的真值，只钉名单的四条规矩——
/// ① 每个形状都念得出名字（<see cref="EventPayload.Wire"/>）与明细（<see cref="EventPayload.Detail"/>）；
/// ② 形状里不许留可选位（构造参数不许带默认值、不许可空——「先知道名字才知道用哪几个字段」的档位就是旧文本的病）；
/// ③ 名单里不许有两个名字（同一 <see cref="EventPayload.Wire"/> 下的形状必须靠字段结构分得开，
///    且允许重名 wire 的只有旧文本里本来就分档位的那七族：
///    pending / modifier / activate / reject / queue_drain / slam_shape / apply_effect）；
/// ④ <see cref="AbilityEventLog"/> 在容量边界上报得出 <c>Dropped</c>（「尾窗里没有」≠「没发生」）。
/// </summary>
public class AbilityEventShapeTests
{
    /// <summary>公共层名单里的全部形状。只数 <c>DemoFactory.GugaFramework.Abilities</c> 命名空间——
    /// 场子自己那两发（单挑场的光环越界/出界）住在测试命名空间，不占名单的名额。</summary>
    private static List<Type> GugaFrameworkShapes() => typeof(EventPayload).Assembly.GetTypes()
        .Where(t => t.Namespace == "DemoFactory.GugaFramework.Abilities"
                    && t.IsSealed && t.IsSubclassOf(typeof(EventPayload)))
        .OrderBy(t => t.Name, StringComparer.Ordinal)
        .ToList();

    private static object? SampleValue(Type type)
    {
        if (type == typeof(string)) return "fx.sample";
        if (type == typeof(long)) return 7L;
        if (type == typeof(int)) return 2;
        if (type == typeof(double)) return 1.5;
        if (type == typeof(bool)) return false;
        if (type == typeof(IReadOnlyList<string>)) return new[] { "aa", "bb" };
        if (type.IsEnum) return Enum.GetValues(type).GetValue(0);
        throw new InvalidOperationException($"形状样例值不认识参数类型 {type.Name}——给新形状加字段时在这里补一个固定样例");
    }

    private static EventPayload Instantiate(Type shapeType)
    {
        var ctors = shapeType.GetConstructors();
        ctors.Length.ShouldBe(1, $"{shapeType.Name}：一个形状只准有一个公共构造（多一个就是又开了一条少填字段的通路）");
        var parameters = ctors[0].GetParameters();
        var args = parameters.Select(p => SampleValue(p.ParameterType)).ToArray();
        return (EventPayload)ctors[0].Invoke(args);
    }

    [Fact]
    public void 名单不为空_每一个形状都念得出名字与明细()
    {
        var shapes = GugaFrameworkShapes();
        shapes.Count.ShouldBeGreaterThanOrEqualTo(40, $"只反射到 {shapes.Count} 个形状——名单读少了，这条就成了空跑");
        foreach (var shapeType in shapes)
        {
            var payload = Instantiate(shapeType);
            payload.Wire.ShouldNotBeEmpty($"{shapeType.Name}：Wire 是空的（事件行的第二段没给）");
            payload.Detail.ShouldNotBeEmpty($"{shapeType.Name}：Detail 是空的（样例值渲染不出明细）");
        }
    }

    [Fact]
    public void 每一个形状都不留可选位()
    {
        // 机器查得出的：构造参数里不存在「带默认值的」与「可空的」——
        // 旧文本里那些 til= / target=- / queued=1 的可选档，在形状里各自成了类，字段表上不该再有档位。
        var nullability = new NullabilityInfoContext();
        foreach (var shapeType in GugaFrameworkShapes())
        {
            foreach (var parameter in shapeType.GetConstructors()[0].GetParameters())
            {
                var name = $"{shapeType.Name}.{parameter.Name}";
                parameter.HasDefaultValue.ShouldBeFalse($"{name} 带了默认值——形状里不许留「可以不填」的档位（缺哪个字段就该少哪条形状）");
                Nullable.GetUnderlyingType(parameter.ParameterType).ShouldBeNull($"{name} 是可空值类型——同一类事件的可选档没自立成类");
                if (!parameter.ParameterType.IsValueType)
                {
                    nullability.Create(parameter).WriteState.ShouldNotBe(NullabilityState.Nullable, $"{name} 是可空引用——同一类事件的可选档没自立成类");
                }
            }
        }
    }

    [Fact]
    public void 名单里不许有两个名字()
    {
        // 「两个名字」的机器口径：拿同一组样例值渲染，(Wire, Detail) 结构完全一样的两条形状 = 名单撞名。
        // wire 重名只许出现在旧文本里本来就分档位的那几族（每一族靠 Detail 结构分得开）：
        // pending / modifier / activate / reject / queue_drain / slam_shape，加上 apply_effect（x1 与 x1 instant 两档）。
        //
        // 这七族今天定着不收紧（用户确认，第 3 条）：收紧 = 给「带尾巴」那一档自立一个名字，
        // 改的是事件行文案，而它换来的「按名字数几条」这件事已经被按形状数（Count<T>()）顶掉了，没有买家。
        // 这张表不会自己长——新增一族当场红，所以它不是遗留问题，别当坑来"修"。
        var allowedSharedWires = new[]
        {
            "pending", "modifier", "activate", "reject", "queue_drain", "slam_shape", "apply_effect",
        };
        var rendered = GugaFrameworkShapes()
            .Select(t => (Type: t, Payload: Instantiate(t)))
            .ToList();

        var names = rendered.Select(r => r.Type.Name).ToList();
        names.Distinct(StringComparer.Ordinal).Count().ShouldBe(names.Count, "反射结果里有同名类型（不该出现，出现就是名单读重了）");

        foreach (var group in rendered.GroupBy(r => r.Payload.Wire))
        {
            var details = group.Select(r => r.Payload.Detail).ToList();
            details.Distinct(StringComparer.Ordinal).Count().ShouldBe(details.Count, $"wire「{group.Key}」下有两条形状渲染出完全一样的行（{string.Join(" / ", group.Select(r => r.Type.Name))}）" +
                "——名单里有了两个名字，就会漂成两回事");
            if (group.Count() > 1)
            {
                allowedSharedWires.ShouldContain(_e => Equals(_e, group.Key), $"wire「{group.Key}」重名了，而它不在允许重名的七族里（pending/modifier/activate/reject/queue_drain/slam_shape/apply_effect）");
            }
        }
    }

    [Fact]
    public void 容量边界上读数报得出丢了哪些()
    {
        // 2001 条进 2000 格的环形：最旧那条被挤出去，Dropped/Kept/Count/Tail 四个读数必须说实话。
        var log = new AbilityEventLog { Capacity = 2000 };
        for (var i = 0; i < 2001; i++)
        {
            log.Add(new AbilityEvent(i + 1, "u01", new EffectExpired("fx.slow")));
        }

        log.Total.ShouldBe(2001, "总数要含被挤掉的");
        log.Dropped.ShouldBe(1, "丢了 1 条就要报 1，不许静默");
        log.KeptCount.ShouldBe(2000);
        log.Count<EffectExpired>().ShouldBe(2000, "按形状数只数留着的——配合 Dropped 才是完整读数");
        log.Tail(2001).Count.ShouldBe(2000, "尾窗最多给到留着的条数");
        log.Tail(2001)[0].Tick.ShouldBe(2, "被挤掉的是最旧的（第 1 步那条），留下的从第 2 步起");
        var head = log.TailText(1).Split('\n')[0];
        head.ShouldBe("dropped=1 total=2001 kept=2000", "读数头一行要把三格都念出来");
    }
}
