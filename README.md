# ZEventSystem

## 介绍

ZEventSystem 是一个高性能的 Unity 事件系统，基于泛型委托组合实现 **O(1) 触发**（无 `DynamicInvoke`），支持字符串/枚举事件名、一对多广播（`UnityAction`）、一对一查询（`Func` 返回值）、按监听者精确移除，以及编辑器调试窗口。

### 核心特性

| 特性 | 说明 |
|------|------|
| **O(1) 触发** | 内部维护组合委托（`Delegate.Combine`），触发时直接 `Invoke`，无反射开销 |
| **多种事件名** | 支持 `string`、任意 `enum`、内置 `EventEnumType` |
| **一对多广播** | `AddListener` / `EventTrigger` — 同一事件可注册多个监听者 |
| **一对一查询（TR）** | `AddListener_TR` / `EventTrigger_TR` — 带返回值的事件，同一事件仅允许一个监听者 |
| **精确移除** | `RemoveListener`（按委托精确移除）、`RemoveOne`（移除某监听者在某事件上的所有注册）、`RemoveALL`（移除某监听者的全部注册） |
| **生命周期自动清理** | `UnRegisterWhenDestroyed` / `UnRegisterWhenDisabled` 自动在 `OnDestroy` / `OnDisable` 时解绑 |
| **OnlyOneID 池** | `IOnlyOneID` 接口提供对象池友好的唯一 ID 分配与回收 |
| **枚举键缓存** | `enum → string` 转换结果自动缓存，支持任意底层类型（`byte`/`ushort`/`uint`/`ulong`） |
| **编辑器调试窗口** | `Tools → ZEventSystem → 事件中心调试窗口`，实时查看所有注册、触发统计、手动触发/移除 |

## 软件架构

```
ZEventSystem/
├── EventCenter.cs              # 核心静态类，包含所有注册/触发/移除 API
├── EventEnumType.cs            # 内置事件枚举定义（可按需扩展）
├── ClearActionsOnDestroy.cs    # OnDestroy 时自动清理注册的 MonoBehaviour
├── ClearActionsOnDisable.cs    # OnDisable 时自动清理注册的 MonoBehaviour
├── Editor/
│   └── EventCenterDebugWindow.cs  # 编辑器调试窗口
├── Test/
│   ├── EventCenterTest.cs      # 使用示例 & 测试脚本
│   └── Test.unity              # 测试场景
└── ZEventtSystem.asmdef        # Assembly Definition
```

### 命名空间

所有核心 API 位于 `ZEventSystem` 命名空间下。

## 安装教程

1. 将 `ZEventSystem` 文件夹整体放入项目的 `Assets` 目录下（或通过 Unity Package Manager 引用）。
2. 确保项目中已引用 `UnityEngine.Events`（Unity 内置，无需额外安装）。
3. 在需要使用事件系统的脚本中添加：
   ```csharp
   using ZEventSystem;
   ```

## 使用说明

### 1. 实现 IListener 接口

任何需要监听事件的类都必须实现 `IListener` 接口：

```csharp
using UnityEngine;
using ZEventSystem;

public class MyComponent : MonoBehaviour, IListener
{
    // IListener 要求的属性，默认实现即可
    public int Rid { get; set; }
}
```

> **提示**：`ListenerName` 已有默认实现，会根据 `IOnlyOneID`、`UnityEngine.Object` 或 `GetHashCode()` 自动生成唯一标识。

### 2. 注册事件（一对多广播）

```csharp
private void OnEnable()
{
    // 字符串事件名
    this.AddListener("OnPlayerHit", OnPlayerHit);

    // 带参数
    this.AddListener<int, string>("OnScoreChanged", OnScoreChanged);

    // 枚举事件名（推荐，类型安全）
    this.AddListener(EventEnumType.SceneLoadComplete, OnSceneLoaded);
}
```

### 3. 触发事件

```csharp
// 无参
EventCenter.EventTrigger("OnPlayerHit");

// 带参
EventCenter.EventTrigger("OnScoreChanged", 100, "Bonus!");

// 枚举
EventCenter.EventTrigger(EventEnumType.SceneLoadComplete);
```

### 4. 注册带返回值的事件（一对一查询）

```csharp
// 注册
this.AddListener_TR<int>("GetPlayerHealth", () => _health);

// 触发并获取返回值
int hp = EventCenter.EventTrigger_TR<int>("GetPlayerHealth");

// 带参数的查询
this.AddListener_TR<string, bool>("CanAfford", (item) => _gold >= GetPrice(item));
bool canBuy = EventCenter.EventTrigger_TR<string, bool>("CanAfford", "Sword");
```

> **注意**：TR（Query）事件为一对一模式，同一事件名仅允许一个监听者。重复注册会覆盖前一个。

### 5. 移除事件

```csharp
// 精确移除某个委托
this.RemoveListener("OnPlayerHit", OnPlayerHit);

// 移除某监听者在某事件上的所有注册
this.RemoveOne_ThisObjListenter("OnPlayerHit");

// 移除某监听者的全部注册（推荐在 OnDisable 中调用）
this.RemoveALL_ThisObjListenter();
```

### 6. 生命周期自动清理

```csharp
private void OnEnable()
{
    this.AddListener("OnPlayerHit", OnPlayerHit)
        .UnRegisterWhenDestroyed();  // OnDestroy 时自动解绑

    // 或者
    this.AddListener("OnPlayerHit", OnPlayerHit)
        .UnRegisterWhenDisabled();   // OnDisable 时自动解绑
}
```

> **推荐**：优先使用 `RemoveALL_ThisObjListenter()` 手动在 `OnDisable` 中解绑，避免额外 `MonoBehaviour` 带来的性能开销。

### 7. 使用枚举事件名

支持任意 `enum` 类型，内部自动缓存为 `"{EnumType.FullName}.{ValueName}"` 字符串：

```csharp
public enum GameEvents
{
    PlayerDied,
    LevelUp,
    ItemCollected
}

// 注册
this.AddListener(GameEvents.LevelUp, OnLevelUp);

// 触发
EventCenter.EventTrigger(GameEvents.LevelUp);
```

### 8. 清除所有事件

```csharp
// 清除所有已注册事件（通常在场景切换或游戏重置时调用）
EventCenter.Clear();
```

### 9. OnlyOneID（对象池友好）

对于通过对象池管理的对象，实现 `IOnlyOneID` 接口可获得稳定的唯一 ID：

```csharp
public class PooledEnemy : MonoBehaviour, IListener, IOnlyOneID
{
    public int Rid { get; set; }

    private void OnEnable()
    {
        this.Init();  // 分配 ID
        this.AddListener(GameEvents.PlayerDied, OnPlayerDied);
    }

    private void OnDisable()
    {
        this.RemoveALL_ThisObjListenter();  // 自动释放 ID
    }
}
```

### 10. 编辑器调试窗口

在编辑器菜单中打开：**Tools → ZEventSystem → 事件中心调试窗口**

功能包括：
- 实时查看所有已注册事件及监听者
- 按事件名搜索/过滤
- 查看触发次数统计与最后触发时间
- 手动触发无参事件
- 手动移除指定注册
- 查看失败原因（未找到事件、委托为空、异常等）

## API 速查表

| 方法 | 说明 |
|------|------|
| `AddListener(eventName, action)` | 注册无返回值事件（一对多） |
| `AddListener_TR(eventName, func)` | 注册带返回值事件（一对一） |
| `EventTrigger(eventName, ...)` | 触发无返回值事件 |
| `EventTrigger_TR(eventName, ...)` | 触发带返回值事件并获取结果 |
| `RemoveListener(eventName, action)` | 精确移除指定委托 |
| `RemoveOne_ThisObjListenter(eventName)` | 移除当前监听者在某事件上的所有注册 |
| `RemoveALL_ThisObjListenter()` | 移除当前监听者的全部注册 |
| `UnRegisterWhenDestroyed()` | OnDestroy 时自动解绑 |
| `UnRegisterWhenDisabled()` | OnDisable 时自动解绑 |
| `Clear()` | 清除所有事件注册 |
| `Init()` / `ReleaseID()` | OnlyOneID 分配与回收 |

所有 `AddListener`、`RemoveListener`、`EventTrigger`、`EventTrigger_TR` 方法均提供 `string` 和 `enum` 两种事件名重载，参数支持 0~3 个。

## 参与贡献

1. Fork 本仓库
2. 新建 Feat_xxx 分支
3. 提交代码
4. 新建 Pull Request