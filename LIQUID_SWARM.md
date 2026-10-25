# GDScript 液体白猫群

入口：`scenes/liquid_cat_swarm_lab.tscn`。F6 运行，根节点的 `cat_count` 默认 100。

## 实现

全部游戏脚本采用 GDScript。`scripts/liquid_cat_swarm.gd` 负责群猫、物理、合并和交互；`scripts/liquid_cat_surface.gd` 负责裁剪后的像素身体及五官。向量运算使用 Godot 原生 `Vector2`，无需 .NET SDK 或程序集构建。

每只猫保留 85 个等质量粒子，每物理帧 3 个子步、每子步 2 轮约束。保留压力、近压力、黏性、内聚、重力、地形碰撞、水状态、蹲下、游动、跳跃、水花和主动聚合。

水状态使用更宽的显示核，补齐不连接外部且不包含固体的小型封闭空隙。五官优先跟随当前水团，限制局部移动，并延迟放大以减少大小反复变化。身体过薄时缩小五官，不能容纳时不悬空绘制。粒子物理状态不因渲染而改变。

每猫使用空间网格查询邻居，按 i/j 顺序修正。独立猫的内部求解和表面生成可通过 `WorkerThreadPool` 分组执行；等待组任务完成后再执行接触、合并或纹理上传。跨猫排斥使用统一位置快照和独立修正缓冲。纹理上传、场景操作和绘制保留在主线程。

只为身体包围区域分配图像，所有猫共用地形遮挡缓存。先绘制所有身体，再绘制耳朵和五官；接触边缘隐藏重复描边。`UseThreads` 可以关闭，用于比较串行与并行结果。

## 接触合并

实验场默认开启 `merge_on_contact`；基础发射器的 `MergeOnContact` 默认关闭。

实际接触的猫群连通组会重采样为 85 个加权粒子。质量份数相加，二维线性尺度为质量份数平方根；压力核、间距、碰撞半径同步放大。修正重采样引入的质心和平均速度误差，仍执行地形投影。

每个合并体只有一套五官。选中成员优先继承身份和交互状态。没有实现分裂。`CatCount` 表示目标初始质量份数；`Metrics().cats` 表示实际个体数，`mass_units` 表示总质量份数。改变目标数量或重置会重建初始猫群。

合并模式是低精度原型：合并体粒子更粗，可能堵住原本可通过的窄漏斗，显示面积也只是近似维持。

## 操作

- 左键选择、拖拽命中猫，只施加外力。
- 右键命中猫：水花或接地跳跃。
- 选中后 AD 移动、WS 游动或蹲下、空格水花或跳跃、E 聚合、F 切换水状态。
- T 切换全部猫的水状态；M 切换合并模式并重置；Tab 暂停物理；R 重置。
- 1–5：堆放、漏斗、连通容器、移动压板、瀑布台阶。
- B 开关背包，Esc 关闭背包。

## 接入

实例化 `scenes/liquid_cat_swarm.tscn`，在启动前配置 `CatCount`、`WorldSize` 和 `SpawnRect`。保持群猫节点单位缩放。

为兼容已有场景和调用者，保留 PascalCase 导出属性及方法，包括 `SetObstacles`、`ResetPopulation`、`ResetCat`、`SetCatAxes`、`SetCatGather`、`SetCatWater`、`JumpCat`、`Advance`、`RenderNow`、`Snapshot` 和 `Metrics`。

`SetObstacles(Array[PackedVector2Array])` 接收群猫局部坐标中的顺时针凸地形。地形变化后重新调用；不自动读取 TileMap，不向原生刚体施加反作用力。

`AutonomousBehavior` 为可选独立待机/游走行为，`BehaviorHz` 只控制决策频率。`CatInteracted(cat_id)` 为互动信号。方法 index 是数组下标，区别于生命周期内递增的 cat_id。

## 运行和验证

使用 Godot 4.7.2，标准版或 Mono 版均可打开本项目；无需 C# 构建步骤。

```powershell
godot --headless --path . --script res://scripts/verify_liquid_swarm.gd
godot --headless --path . --script res://scripts/verify_cat_merge.gd
```

迁移后的 GDScript 性能不等同于原编译实现。100 只互不合并的高精度猫仍有较高 CPU 开销，不能沿用旧版 FPS 测量作为当前保证；默认接触合并能降低堆积后的粒子预算。性能回归会输出本机实测 CPU 阶段耗时。

原 `scripts/liquid_cat.gd` 与七房间实验室继续作为求解行为参考；刚体 `cat_crowd_lab.tscn` 是独立的早期试验。

迁移前实现、说明及验证数据已保留在项目外的 `../gpt` 工作区供溯源。
