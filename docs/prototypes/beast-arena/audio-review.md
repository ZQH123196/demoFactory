# 音效资产与技术样本评审（ticket 17 / 规格 T16）

本文登记事实与实测数字：声件清单、触发语义对照、响度与峰值实测、四段技术样本、重跑命令与遗留项。约束句的出处在 `AGENTS.md` 契约索引与规格「11. 音效」，这里只引用不新立。

## 1. 合成器与重跑命令

全部声音由 `src/Prototypes/BeastArena/Tools/AudioGen.cs` + `AudioSynth.cs` 从零合成，不引用任何外部素材。算术一律 double、随机来自显式播种的 xorshift，同参数重跑逐字节相同（同机同运行时）。

```
# 生成（幂等：内容相同不改写文件）
dotnet run --project src/Tools -- audiogen BeastArena .
# 逐字节校验（重跑并与盘上文件比对，全一致退 0）
dotnet run --project src/Tools -- audiogen BeastArena . verify
# 引擎导入（生成 .import 伴随文件）
"C:\Users\ZQH12\Desktop\software\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path . --import
# 复测响度与 true peak（单文件）
ffmpeg -hide_banner -nostats -i src/Prototypes/BeastArena/Audio/<文件>.wav -filter_complex "ebur128=peak=true" -f null -
```

生成器的测量表落 `.cache/BeastArenaAudio/measures.tsv`（临时产物，不进版本库）。

## 2. 八类清单与触发语义对照

cue 名单的唯一出处是 `src/Prototypes/BeastArena/Scripts/Audio/AudioCatalog.cs`；cue 名与 `.scratch` 声原型（audio-prototype.html）的 16 个单项事件同名。事件名按对接面清单（interface.md）对齐，接线在 T17 做。

| 类 | 文件（`src/Prototypes/BeastArena/Audio/`） | 档位 | 触发语义（interface.md 事件） |
| --- | --- | --- | --- |
| UI | `ui_click.wav` `ui_deny.wav` | Feedback | 动词受理返回值；拒绝 → E-03 `EatRejected` / 各动词 `RejectReason` |
| 建造与交易 | `build_done.wav` `trade_done.wav` | Feedback | 建造/修理成交（G-10）；入库/出售 → E-08 `CrystalWarehoused` / E-10 `CrystalSold` |
| 吞食与魔晶 | `beast_swallow.wav` `crystal_drop.wav` | Growth | E-02 `EatCompleted`；E-07 `CrystalsProduced` |
| 形态与进阶 | `form_shift.wav` `tier_advance.wav` | Growth | E-13 `FormSwitched` / E-12 `FormUnlockedEvent`；E-14 `TierAdvanced`（G-2 仪式开始同件承接） |
| 攻击与受击 | `attack_shot.wav` `impact_hit.wav` | Feedback | A-H4 出手那一刻；A-H5 受方（G-13 逐次受击事件落地前按结算配对触发） |
| 警报与胜负 | `alarm_warning.wav` `alarm_critical.wav` `result_victory.wav` `result_defeat.wav` | Alert | E-21 `PhaseEntered(NormalDefence/FinalDefence)`（G-6）；镇政府危急读数；E-17 `DefenceSettled(won)` |
| 巨兽步伐 | `beast_step.wav` | Feedback | 巨兽移动节奏（非事件驱动，随速度铺） |
| 环境与广播 | `amb_dock.wav` `amb_sail.wav` `amb_defense.wav`（循环床）`broadcast_horn.wav`（号角） | Ambience / Growth | 停靠·航行·防守态换床；世界内广播只号角、无人员语音 |

无背景音乐轨。环境循环 8/8/6 秒（头 1 秒与尾 1 秒交叉淡化，可无缝循环）；单条最长 2.8 秒，全部在 5 秒内。

## 3. 混音策略件

`src/Prototypes/BeastArena/Scripts/Audio/AudioMixPolicy.cs`（纯 C#，零 Godot 类型，不进真值）：

- 相同 cue 的 Feedback 声在 80 毫秒窗口（左闭右开）内合并为一次。
- Growth / Alert 声线在发时，新的 Feedback 请求让位；Growth / Alert 自身直接发并列出应停的在发 Feedback 声线（抢占）。
- 时间以毫秒整数由调用方递进（假时钟），策略本身不自持时钟。

测试：`AudioMixPolicyTests.cs`（窗口差一、让位、抢占名单、恢复共 9 条）+ `AudioAssetTests.cs`（资产离线校验 56 条：WAV 头 48k/24-bit、true peak、直流、静音段、削波、响度关系、四段样本时长与电平、cue 表契约）。xunit 里不调 ffmpeg、不调任何外部服务；ffmpeg 只服务本文档的复测。

## 4. 单条资产实测（48 kHz / 24-bit PCM 单声道）

响度列为生成器 K 计权（BS.1770，全缓冲无门限）实测；true peak 列为 4 倍过采样估计，并与 ffmpeg ebur128 的 `TPK` 逐条核对一致。直流为样本均值绝对值上界；削波计数 = |样本| ≥ 0.999 的条数（全部为 0）。

| 文件 | 秒 | 响度 LUFS | true peak dBTP | 直流 | 削波 |
| --- | ---: | ---: | ---: | ---: | ---: |
| ui_click | 0.09 | -28.0 | -16.6 | <1e-17 | 0 |
| ui_deny | 0.36 | -28.0 | -17.1 | <1e-17 | 0 |
| build_done | 0.80 | -28.0 | -10.8 | <1e-17 | 0 |
| trade_done | 0.50 | -28.0 | -14.3 | <1e-17 | 0 |
| beast_swallow | 1.00 | -25.0 | -10.5 | <1e-17 | 0 |
| crystal_drop | 0.80 | -25.0 | -10.0 | <1e-17 | 0 |
| form_shift | 1.40 | -22.0 | -6.3 | <1e-17 | 0 |
| tier_advance | 2.60 | -22.0 | -8.9 | <1e-17 | 0 |
| attack_shot | 0.28 | -28.0 | -15.4 | <1e-17 | 0 |
| impact_hit | 0.40 | -28.0 | -13.3 | <1e-17 | 0 |
| alarm_warning | 1.75 | -18.0 | -8.6 | <1e-17 | 0 |
| alarm_critical | 1.30 | -18.4 | -4.0 | <1e-17 | 0 |
| result_victory | 2.80 | -18.0 | -4.9 | <1e-17 | 0 |
| result_defeat | 2.50 | -18.0 | -5.0 | <1e-17 | 0 |
| beast_step | 0.30 | -28.0 | -12.3 | <1e-17 | 0 |
| broadcast_horn | 1.50 | -19.0 | -5.3 | <1e-17 | 0 |
| amb_dock | 8.00 | -36.0 | -25.3 | <1e-17 | 0 |
| amb_sail | 8.00 | -36.0 | -25.8 | <1e-17 | 0 |
| amb_defense | 6.00 | -36.0 | -25.6 | <1e-17 | 0 |

单条 true peak 全部 ≤ -4.0 dBTP（判据 -3 dBTP 有余量）。

### 响度关系实测（ffmpeg ebur128 积分响度）

| 对照 | ffmpeg I（LUFS） | 差（LU） |
| --- | --- | ---: |
| alarm_warning vs amb_dock | -18.3 vs -36.0 | 17.7 |
| alarm_critical vs amb_sail | -19.4 vs -36.0 | 16.6 |
| result_victory vs amb_defense | -18.0 vs -36.1 | 18.1 |
| result_defeat vs amb_dock | -18.4 vs -36.0 | 17.6 |
| build_done vs amb_dock | -26.8 vs -36.0 | 9.2 |
| impact_hit vs amb_defense | -29.3 vs -36.1 | 6.8 |
| trade_done vs amb_sail | -29.3 vs -36.0 | 6.7 |

警报比环境底声实测 ≥ 16.6 LU（判据 ≥6 LU）；普通操作反馈 ≥ 6.7 LU（判据 ≥3 LU）。
注：时长不足 0.4 秒的短件（ui_click / ui_deny / attack_shot / beast_step）在 BS.1770 的 400 毫秒分析窗下 ffmpeg 积分响度不可测（报 -70），按生成器 K 计权实测为 -28.0 LUFS，比环境底声高 8 LU。

## 5. 四段固定技术样本

路径 `src/Prototypes/BeastArena/Audio/Samples/`，48 kHz / 24-bit 单声道，单位增益叠加各源件（相对响度差即源件档位差），头尾 10 毫秒总淡化。防守样本在 20 秒胜利后床声由 `amb_defense` 交叉接管为 `amb_dock`（胜利收束、回落非战斗环境）。

| 样本 | 秒 | 排布（cue @ 毫秒） | ffmpeg I | ffmpeg TP dBFS | 直流 | 削波 |
| --- | ---: | --- | ---: | ---: | ---: | ---: |
| sample_dock | 20.0 | amb_dock 床；ui_click@500、build_done@2000、ui_deny@5000、ui_click@5250、trade_done@7000、broadcast_horn@14000、ui_click@17500 | -28.7 | -5.0 | <1e-17 | 0 |
| sample_sail | 20.0 | amb_sail 床；beast_step@1000/3000/7000、beast_swallow@5500、crystal_drop@9000、attack_shot@11000、impact_hit@11160、form_shift@14500 | -29.8 | -5.9 | <1e-17 | 0 |
| sample_growth | 16.0 | amb_dock 床；crystal_drop@1000、trade_done@2800、form_shift@5000、tier_advance@8000、ui_click@14000 | -26.9 | -6.1 | <1e-17 | 0 |
| sample_defense | 24.0 | amb_defense 床 0→20000 与 amb_dock 19000→24000 交叉；alarm_warning@500、attack_shot@4000、impact_hit@4150、attack_shot@6000、impact_hit@6090（两条相隔 90 毫秒，出 80 合并窗）、alarm_critical@12000、result_victory@20000 | -19.4 | -3.9 | <1e-17 | 0 |

最终混音 true peak 实测 ≤ -3.9 dBTP（判据 ≤ -1 dBTP）。样本内最长静音段 ≤ 50 毫秒（离线断言 500 毫秒上限）。四段样本随 `audiogen` 一起确定性重跑，`verify` 逐字节覆盖。

## 6. 实测汇总命令

```
for w in src/Prototypes/BeastArena/Audio/*.wav src/Prototypes/BeastArena/Audio/Samples/*.wav; do
  ffmpeg -hide_banner -nostats -i "$w" -filter_complex "ebur128=peak=true" -f null - 2>&1 |
    sed -n '/Summary:/,$p' | grep -E "I:|    Peak:"
done
```

## 7. 遗留项

- 环境床 cue 的换床与音量包络接线属 T17；本次只提供床文件与 `AudioMixPolicy` / `AudioCatalog` 两个纯 C# 件。
- `.glb` 之外本次首次跑引擎导入，顺带生成了历史遗留脚本的 `.cs.uid` 未跟踪文件（约百余件）——是否入库归用户裁决，本 ticket 只提交自己新增六件源文件的 `.uid`。
- 短件的 ffmpeg 积分响度不可测（BS.1770 窗口限制）；如需真机核对短件响度，用 `ebur128` 的 momentary 序列或离线 K 计权（`AudioAssetTests` 已断言关系）。
