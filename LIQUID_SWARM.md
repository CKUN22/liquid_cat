# C# 液体白猫群

项目自有游戏、UI、实验场和验证代码统一使用 C#。第三方 Godot AI 插件保留自带脚本。
主规则见 `AGENTS.md` 与 `../gpt/AGENTS.md`，后续不再新增或维护自有 GDScript。

## 运行

使用 Godot 4.7.2 Mono/.NET 和 .NET 8 SDK。普通非 .NET Godot 无法运行本项目的 C# 节点。

```powershell
dotnet build liquid_cat.csproj
```

打开 `scenes/liquid_cat_swarm_lab.tscn` 后按 F6。根节点 `CatCount` 默认100，`MergeOnContact` 默认开启。
单猫七房间入口为 `scenes/liquid_cat_lab.tscn`，同样使用编译后的求解器。

## 实现

- `scripts/LiquidCatSwarm.cs`：粒子物理、群猫接触、质量合并、行为和裁剪像素渲染。
- `scripts/FastVector2.cs`：内联向量运算，降低高频数学调用开销。
- `scripts/LiquidCat.cs`：单猫适配，复用同一求解器和渲染器。
- `scripts/LiquidCatLab.cs` / `LiquidCatSwarmLab.cs`：单猫七房间及群猫五地形实验场。
- `scripts/inventory/`：C# 背包、物品资源和拖拽格子。
- `scripts/CatCrowd.cs` 等：独立的早期刚体猫试验，也已改为 C#。
- `scripts/VerificationRunner.cs`：C# 自动验证入口。

每猫85粒子，每物理帧3子步、每子步2轮约束。保留重力、压力、近压力、黏性、内聚、蹲下、游动、跳跃、水花、主动聚合和地形碰撞。

水状态补齐小型封闭空隙，五官跟随当前水团、限制局部移动并延迟放大；不能容纳五官时不悬空绘制。身体与五官使用分别裁剪的像素缓冲。独立求解和CPU表面构建最多并行8个工作任务，纹理上传及场景操作在主线程。

## 接触合并

基础发射器默认关闭合并，实验场默认开启。实际接触的猫群连通组会重采样为85个加权粒子；质量份数相加，二维尺度按质量平方根放大。合并继承一个身份和一套五官，未实现分裂。

`CatCount` 是目标初始质量份数，`Metrics().cats` 是实际个体数，`mass_units` 是总质量份数。重置或修改目标数量会重建初始猫群。

合并模式降低精度：粒子、碰撞半径与显示像素变粗，可能堵住原本可通过的窄通道。

## 操作及接口

- 左键选择/拖拽，右键水花或接地跳跃。
- 选中后 AD 移动、WS 游动/蹲下、空格跳跃/水花、E 聚合、F 切换水状态、V 粒子调试。
- 群猫场：T 全部水化，M 切换合并并重置，Tab 暂停物理，R 重置，1–5 切换地形。
- 单猫场：1–7 切换房间，P 压板，Tab 暂停，R 重置。
- B 开关背包，Esc 关闭背包。Unifont 默认字体、整数倍界面缩放保持不变。

实例化 `scenes/liquid_cat_swarm.tscn`，运行前配置 `CatCount`、`WorldSize`、`SpawnRect`。
通过 `SetObstacles(Array<Vector2[]>)` 提交局部坐标的顺时针凸地形；移动后重新提交。不自动读取 TileMap，不向原生刚体施加反作用力。

保留 `ResetPopulation`、`ResetCat`、`SetCatWater`、`SetCatAxes`、`SetCatGather`、`JumpCat`、`Advance`、`RenderNow`、`Snapshot`、`Metrics`、`CatInteracted` 等 C# 接口。

## 验证和性能

```powershell
dotnet build --no-restore
Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . res://scenes/verification.tscn
```

完整 C# 验证覆盖：背包B/Esc、暂停恢复、堆叠/交换/满包；七地形×两状态；85/85粒子漏斗通水；水池、水花、聚合；240帧五官稳定；百猫接触和数量变化；隔墙不合并、链式合并和质量守恒；刚体猫行为；实验场实际加载。

恢复后的本机100猫未合并CPU流程采样：P50约10.5ms、P95约11.8ms；不代表实际GPU显示FPS。历史GD对照见 [性能报告](PERFORMANCE_GD_MIGRATION.md)，该报告记录的是已撤回的GD迁移版本。

历史源码、日志和截图保留在 `../gpt`，本轮备份目录为 `before-all-csharp`。
