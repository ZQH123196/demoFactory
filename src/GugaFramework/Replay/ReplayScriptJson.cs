#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DemoFactory.GugaFramework.Replay;

/// <summary>
/// 重放脚本 json 序列化 — 唯一一份口径，录制端（写）与重放端（读）共用。
///
/// 手写最小 json（不引依赖）：结构固定为
/// {"seed":N,"assertLevel":N,"entries":[{"tick":N,"seq":N,…},…],
///  "checkpoints":[{"tick":N,"seq":N,"label":"…","snapshot":"…"},…]}
/// seq 是同一步内的录制顺序（重放端 (步号, 录制序号) 稳定排序执行）；
/// startTick/startSnapshot 是录制起点（运行中世界接着录时 &gt;0，重放端先对齐并断言起点快照；
/// 从开局录则无这两键/缺省 0 与 null）。
/// assertLevel 是检查点断言档位（Phase 4.1：1=L1 不变量·不进脚本 / 2=L2 语义 / 3=L3 逐字）；
/// 写端恒写、读端缺键默认 3（L3 逐字，存量脚本零迁移）。
/// 字符串转义走 JSON 最小集（引号/反斜杠/控制符）——cmd/args 含 '|' 分隔符但不含引号，
/// snapshot 含引号（单位名），必须转义。
/// </summary>
public static class ReplayScriptJson
{
    // ================= 写 =================

    public static string Write(ReplayRunner.ReplayScript s)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{{\"seed\":{s.Seed},\"assertLevel\":{(int)s.AssertLevel},\"entries\":[");
        for (var i = 0; i < s.Entries.Count; i++)
        {
            var e = s.Entries[i];
            if (i > 0) sb.Append(',');
            sb.Append(CultureInfo.InvariantCulture,
                $"{{\"tick\":{e.Tick},\"seq\":{e.Seq},\"cmd\":{Str(e.Cmd)},\"args\":{Str(e.Args)},\"ok\":{(e.Ok ? "true" : "false")}}}");
        }
        sb.Append($"],\"startTick\":{s.StartTick}");
        if (s.StartSnapshot != null)
            sb.Append($",\"startSnapshot\":{Str(s.StartSnapshot)}");
        sb.Append(",\"checkpoints\":[");
        for (var i = 0; i < s.Checkpoints.Count; i++)
        {
            var c = s.Checkpoints[i];
            if (i > 0) sb.Append(',');
            sb.Append(CultureInfo.InvariantCulture,
                $"{{\"tick\":{c.Tick},\"seq\":{c.Seq},\"label\":{Str(c.Label)},\"snapshot\":{Str(c.Snapshot)}}}");
        }
        sb.Append("]}");
        // 末尾换行：落盘件按仓库文本口径收尾。没有它的话，每次跑全量都把工作区弄脏一次 ——
        // 那两条「录制落盘读回重放」用例会就地重录 Tests/Scripts 下的脚本，重录件比在库件少一个
        // 末尾换行，git 就报 \ No newline at end of file。实测踩过两次：一次表现为「红自己变绿了」，
        // 一次是 git diff 里那份单行巨型 json 刷屏。读端逐处 SkipWs，多一个换行不影响解析。
        sb.Append('\n');
        return sb.ToString();
    }

    // ================= 读 =================

    public static ReplayRunner.ReplayScript Read(string json)
    {
        var p = new Parser(json);
        p.Expect('{');
        int seed = 0;
        long startTick = 0;
        string? startSnapshot = null;
        var assertLevel = ReplayRunner.AssertLevel.Literal; // 缺键默认 L3（存量脚本零迁移）
        List<ReplayRunner.JournalEntry> entries = new();
        List<ReplayRunner.Checkpoint> checkpoints = new();
        while (p.Peek() != '}')
        {
            var key = p.ReadString();
            p.Expect(':');
            switch (key)
            {
                case "seed":
                    seed = (int)p.ReadNumber();
                    break;
                case "assertLevel":
                    assertLevel = (ReplayRunner.AssertLevel)(int)p.ReadNumber();
                    break;
                case "startTick":
                    startTick = (long)p.ReadNumber();
                    break;
                case "startSnapshot":
                    startSnapshot = p.ReadString();
                    break;
                case "entries":
                    p.Expect('[');
                    while (p.Peek() != ']')
                    {
                        p.Expect('{');
                        long tick = 0;
                        int seq = 0;
                        string cmd = "", args = "";
                        bool ok = false;
                        while (p.Peek() != '}')
                        {
                            var k = p.ReadString();
                            p.Expect(':');
                            switch (k)
                            {
                                case "tick": tick = (long)p.ReadNumber(); break;
                                case "seq": seq = (int)p.ReadNumber(); break;
                                case "cmd": cmd = p.ReadString(); break;
                                case "args": args = p.ReadString(); break;
                                case "ok": ok = p.ReadBool(); break;
                                default: throw new FormatException($"entries 未知键 {k}");
                            }
                            if (p.Peek() == ',') p.Advance();
                        }
                        p.Expect('}');
                        entries.Add(new ReplayRunner.JournalEntry(tick, cmd, args, ok, seq));
                        if (p.Peek() == ',') p.Advance();
                    }
                    p.Expect(']');
                    break;
                case "checkpoints":
                    p.Expect('[');
                    while (p.Peek() != ']')
                    {
                        p.Expect('{');
                        long tick = 0;
                        int seq = 0;
                        string label = "", snapshot = "";
                        while (p.Peek() != '}')
                        {
                            var k = p.ReadString();
                            p.Expect(':');
                            switch (k)
                            {
                                case "tick": tick = (long)p.ReadNumber(); break;
                                case "seq": seq = (int)p.ReadNumber(); break;
                                case "label": label = p.ReadString(); break;
                                case "snapshot": snapshot = p.ReadString(); break;
                                default: throw new FormatException($"checkpoints 未知键 {k}");
                            }
                            if (p.Peek() == ',') p.Advance();
                        }
                        p.Expect('}');
                        checkpoints.Add(new ReplayRunner.Checkpoint(tick, label, snapshot, seq));
                        if (p.Peek() == ',') p.Advance();
                    }
                    p.Expect(']');
                    break;
                default:
                    throw new FormatException($"顶层未知键 {key}");
            }
            if (p.Peek() == ',') p.Advance();
        }
        p.Expect('}');
        return new ReplayRunner.ReplayScript(seed, entries, checkpoints, startTick, startSnapshot, assertLevel);
    }

    // ---- 最小 json 写出：字符串转义（JSON 标准集）----

    private static string Str(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    // ---- 最小 json 读取：游标式扫描（结构已由 Write 固定，无需通用解析器）----

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;

        public Parser(string s) { _s = s; SkipWs(); }

        public char Peek() => _s[_i];

        public void Advance() { _i++; SkipWs(); }

        public void Expect(char c)
        {
            if (_s[_i] != c)
                throw new FormatException($"json 第 {_i} 字符应为 '{c}'，实为 '{_s[_i]}'");
            _i++;
            SkipWs();
        }

        public string ReadString()
        {
            if (_s[_i] != '"') throw new FormatException($"json 第 {_i} 字符应为字符串，实为 '{_s[_i]}'");
            _i++;
            var sb = new StringBuilder();
            while (_s[_i] != '"')
            {
                var ch = _s[_i++];
                if (ch == '\\')
                {
                    var esc = _s[_i++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: throw new FormatException($"非法转义 \\{esc}");
                    }
                }
                else sb.Append(ch);
            }
            _i++;
            SkipWs();
            return sb.ToString();
        }

        public double ReadNumber()
        {
            var start = _i;
            while (_i < _s.Length && (_s[_i] == '-' || _s[_i] == '+' || char.IsDigit(_s[_i]) || _s[_i] == '.' || _s[_i] == 'e' || _s[_i] == 'E'))
                _i++;
            var v = double.Parse(_s[start.._i], CultureInfo.InvariantCulture);
            SkipWs();
            return v;
        }

        public bool ReadBool()
        {
            if (_s.AsSpan(_i).StartsWith("true")) { _i += 4; SkipWs(); return true; }
            if (_s.AsSpan(_i).StartsWith("false")) { _i += 5; SkipWs(); return false; }
            throw new FormatException($"json 第 {_i} 字符应为 bool，实为 '{_s.Substring(_i, Math.Min(5, _s.Length - _i))}'");
        }

        private void SkipWs()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }
    }
}
