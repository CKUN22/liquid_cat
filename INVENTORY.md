# C# 背包系统

B 打开或关闭；Esc 或右上角 × 关闭。打开时暂停游戏，关闭时恢复此前暂停状态。
24格，支持选中查看说明、堆叠、拖拽移动和交换；拖到界面外保留物品。初始为空。
界面采用项目默认Unifont字体、16px字号、整数倍放大和像素对齐。切换场景保留内容，尚无退出存档。

## 接入

创建 `InventoryItem` 资源，配置 `Id`、`DisplayName`、`Description`、`Icon`、`MaxStack`。相同Id应使用同一物品定义。

```csharp
var inventory = GetNode<Inventory>("/root/Inventory");
int accepted = inventory.AddItem(itemResource, quantity);
// 世界中只移除 accepted 数量，未接收的物品保留。
inventory.RemoveItem(slotIndex, quantity);
```

`Inventory` 自动加载指向 `scripts/inventory/Inventory.cs`；格子使用 `InventorySlot.cs`。
使用 `scenes/verification.tscn` 运行 C# 背包验证。
参考 `Z:/Godot/参考/beibao/inventory-tutorial` 的资源、格子和堆叠结构实现，保留白猫设定。
