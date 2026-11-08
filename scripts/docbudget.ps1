# docbudget.ps1 — AGENTS.md 字节预算守门（项目维护脚本，不进任何 csproj、Godot 不导入）
#
# 目的（遗留账「目的」栏）：让「文档涨到 agent 装不下」这件事变便宜 ——
# 超限时把警告写进文件头，agent 下次载入 AGENTS.md 就当场看见，不必等谁想起来查。
#
# 口径（常量只存这一处，改这里即可，不要散到别处）：
#   硬上限 32768 字节（超了 agent 载入不全）；软线 90% 只报不动。
#   判定用「去掉警告行之后」的字节数 base —— 否则警告行自己把文件顶得更超，
#   且在边界上会反复增删。滞回：base 回到上限的 95%（31129）以下才把警告行删掉。
#
# 安全边界：只动自己那一行。标记行 `<!-- docbudget:warn -->` 出现多于一次就报错退出，
# 绝不碰规则正文。写入走「临时文件 + 原子替换」，保留原 BOM 与换行风格。
#
# 用法：pwsh -NoProfile -File scripts/docbudget.ps1 [-Path <文件>] [-Limit 32768] [-Soft 0.9] [-Reclaim 0.95]
# 返回码：0 正常未动 / 1 超上限（警告行在） / 2 用法或读写错 / 4 标记行重复，未动

[CmdletBinding()]
param(
    [string] $Path    = (Join-Path $PSScriptRoot '..' 'AGENTS.md'),
    [int]    $Limit   = 32768,
    [double] $Soft    = 0.90,
    [double] $Reclaim = 0.95
)

$ErrorActionPreference = 'Stop'
$Marker = '<!-- docbudget:warn -->'
$inv    = [Globalization.CultureInfo]::InvariantCulture

# 按 markdown 标题切节，量每节 UTF-8 字节数 —— 报错要指向「该搬哪一段」，
# 只给一个总数的话，看到红灯也不知道动哪里。
function Get-Sections([string] $text, [int] $top) {
    $m = [regex]::Matches($text, '(?m)^#{1,3} .*$')
    $out = @()
    for ($i = 0; $i -lt $m.Count; $i++) {
        $s = $m[$i].Index
        $e = if ($i + 1 -lt $m.Count) { $m[$i + 1].Index } else { $text.Length }
        $out += [pscustomobject]@{
            节   = $m[$i].Value.Trim()
            字节 = [Text.Encoding]::UTF8.GetByteCount($text.Substring($s, $e - $s))
        }
    }
    $out | Sort-Object 字节 -Descending | Select-Object -First $top
}

try {
    $full = $Path
    if (-not [IO.Path]::IsPathRooted($full)) { $full = Join-Path (Get-Location).Path $full }
    if (-not (Test-Path -LiteralPath $full)) { throw "找不到文件：$full" }

    $bytes = [IO.File]::ReadAllBytes($full)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $total = $bytes.Length

    $text = [Text.Encoding]::UTF8.GetString($bytes)
    if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }

    # 标记行只认行首整行（允许缩进），不匹配正文里提到这个字符串的地方
    $rx    = [regex]('(?m)^[ \t]*' + [regex]::Escape($Marker) + '.*(?:\r?\n)?')
    $marks = $rx.Matches($text)
    if ($marks.Count -gt 1) {
        # 走 stderr 直写而不是 Write-Error：ErrorActionPreference=Stop 会把 Write-Error
        # 变成终止错误，落进下面自己的 catch，退出码就成 2（「用法/读写错」）而不是 4。
        # 消息先拼进变量：`-f` 直接写在 .NET 方法的参数位会被解析成两个实参。
        $msg = "警告行出现 {0} 次（应恰好 0 或 1），本次不动文件。请人工检查：{1}" -f $marks.Count, $full
        [Console]::Error.WriteLine($msg)
        exit 4
    }
    $warnPresent = ($marks.Count -eq 1)

    $base      = if ($warnPresent) { $text.Remove($marks[0].Index, $marks[0].Length) } else { $text }
    $bomBytes  = if ($hasBom) { 3 } else { 0 }
    $baseBytes = $bomBytes + [Text.Encoding]::UTF8.GetByteCount($base)

    $softBytes    = [int][math]::Floor($Limit * $Soft)
    $reclaimBytes = [int][math]::Floor($Limit * $Reclaim)
    $pctBase      = ($baseBytes / $Limit * 100).ToString('0.0', $inv)
    $nl           = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }

    $new    = $text
    $state  = 'OK'
    $action = 'none'

    if (-not $warnPresent -and $total -gt $Limit) {
        # 插在首行（文档标题）之后：标题留在第一行，警告仍在文件最头上
        $line  = ('{0} ⚠️ **AGENTS.md 已超字节上限** —— {1} / {2} 字节（{3}%）。' +
                  'agent 载入可能不全：把最占地方的整段搬进 docs/ 后本行自动消失。')
        $line  = $line -f $Marker, $total, $Limit, $pctBase
        $i     = $text.IndexOf("`n")
        $new   = if ($i -lt 0) { $text + $nl + $line + $nl } else { $text.Insert($i + 1, $line + $nl) }
        $state = 'WARN-ADDED'; $action = 'insert'
    }
    elseif ($warnPresent -and $baseBytes -le $reclaimBytes) {
        $new   = $base
        $state = 'WARN-CLEARED'; $action = 'remove'
    }
    elseif ($warnPresent) {
        # 刷新那行里的数字（文件可能又长了），滞回区内不动它
        $pctTotal = ($total / $Limit * 100).ToString('0.0', $inv)
        $line     = ('{0} ⚠️ **AGENTS.md 已超字节上限** —— {1} / {2} 字节（{3}%）。' +
                     'agent 载入可能不全：把最占地方的整段搬进 docs/ 后本行自动消失。')
        $line     = $line -f $Marker, $total, $Limit, $pctTotal
        $head     = $text.Substring(0, $marks[0].Index)
        $tail     = $text.Substring($marks[0].Index + $marks[0].Length)
        $new      = $head + $line + $nl + $tail
        $state    = 'WARN-KEPT'; $action = 'refresh'
    }

    if ($new -ne $text) {
        $enc = [Text.UTF8Encoding]::new($hasBom)
        $tmp = "$full.tmp-docbudget"
        [IO.File]::WriteAllText($tmp, $new, $enc)
        try   { [IO.File]::Replace($tmp, $full, $null, $true) }
        catch {
            [IO.File]::Copy($tmp, $full, $true)
            Remove-Item -LiteralPath $tmp -Force
        }
    }

    Write-Output ('docbudget {0} state={1} base={2} total={3} limit={4} soft={5} pct={6} warn={7} action={8}' -f
        [IO.Path]::GetFileName($full), $state, $baseBytes, $total, $Limit, $softBytes, $pctBase,
        $(if ($state -eq 'OK') { 'absent' } else { 'present' }), $action)

    if ($state -eq 'WARN-ADDED' -or $state -eq 'WARN-KEPT') {
        Write-Output '最大的几节（搬走哪一段就照这个顺序看）：'
        Get-Sections $base 6 | Format-Table -AutoSize | Out-String | Write-Output
        exit 1
    }
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 2
}
