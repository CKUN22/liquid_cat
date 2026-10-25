# C# 刚体猫试验

独立试验入口：`scenes/cat_crowd_lab.tscn`。这是原生圆形刚体和序列帧动画试验，不是液体猫入口。

- `CatCrowd.cs`：集中管理数量、决策调度和选择/拖拽。
- `CrowdCatBody.cs`：刚体、独立行为状态和动画。
- `CrowdCatBehavior.cs`：共享无状态行为资源，优先级为拖拽、互动反馈、待机/游走。
- `CatCrowdLab.cs`：地形和HUD。

所有个体共用SpriteFrames，状态和随机数独立。左键拖拽、右键跳跃，失去窗口焦点时释放拖拽。
检查器可配置 `CatCount`、`BehaviorHz`、`BehaviorTree` 和 `SpawnRect`；场景文件可覆盖脚本默认值。

该试验不能液体变形、分流或通过小于刚体直径的缝隙。液体关卡请使用 `liquid_cat_swarm_lab.tscn`。
统一C#验证入口为 `scenes/verification.tscn`，覆盖共享动画、行为优先级、数量增减和物理边界。
