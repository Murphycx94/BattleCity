# 阶段 1：单机试驾场 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新建纯 C# 仿真库 `Tank.Sim`（地形、贴地运动、碰撞、炮塔瞄准、弹道），并在 Godot 客户端中做一个能开坦克、第三人称瞄准射击的单机试驾场。

**Architecture:** `Tank.Sim` 不依赖引擎，30Hz 固定步长，`float` 加 `System.Numerics`，坐标轴与 Godot 一致（Y 向上，-Z 为前方，yaw 为正是向左转）。客户端 `SandboxMain` 在本地以 30Hz 驱动 `Simulation`，在前后两个 tick 之间插值渲染。旧的 `Tank.Core` 和旧客户端代码不动。

**Tech Stack:** .NET 8、C# 12、xUnit 2.9、Godot 4.7 .NET 版

**设计依据：** `docs/superpowers/specs/2026-10-13-third-person-tank-design.md`（§2、§3、§4.1、§4.2、§5.2、§9、§12 阶段 1）

---

## 执行前须知

- `dotnet` 不在默认 PATH 中。每个 shell 先执行：`export PATH="$HOME/.dotnet:$PATH"`
- 主工作区有用户未提交的改动。**必须在 worktree 中执行**：
  ```bash
  cd /Users/murphy/Desktop/dev/BattleCity
  git worktree add ../BattleCity-stage1 -b feat/stage1-sandbox
  cd ../BattleCity-stage1
  ```
  下文所有路径都相对于 worktree 根目录。
- `Directory.Build.props` 开启了 `TreatWarningsAsErrors`，`Tank.Sim` 和测试项目中出现任何警告都会导致构建失败。
- `Tank.Sim` 里**不要**建名为 `Math` 的命名空间，否则项目内的 `Math.Clamp` 会解析到它。几何工具统一放在 `Tank.Sim.Geometry`。
- 角度约定：yaw 为 0 时朝向 -Z；yaw 为正是逆时针（从上往下看是向左转），与 Godot 的 `Rotation.Y` 一致。`Steer = +1` 表示向右转，即 yaw 减小。
- 二维向量 `Vector2` 表示地面坐标，`.X` 是世界 X，`.Y` 是世界 **Z**。

## 文件结构

**新建：`src/Tank.Sim/`**

| 文件 | 职责 |
|---|---|
| `Tank.Sim.csproj` | 纯仿真库，无依赖 |
| `SimConstants.cs` | tick 频率、步长、重力 |
| `Geometry/Angles.cs` | 角度换算、环绕、插值、yaw/pitch 与方向向量互转 |
| `Geometry/Obb2.cs` | 地面有向矩形，SAT 穿透向量 |
| `Geometry/Intersect.cs` | 线段与竖直棱柱求交 |
| `Geometry/XorShift.cs` | 种子化随机数 |
| `Terrain/HeightField.cs` | 高度图采样、法线、线段求交 |
| `Terrain/TerrainGenerator.cs` | 种子化分形噪声加山丘 |
| `Maps/Prop.cs` | 物件（v1 只有岩石） |
| `Maps/Battlefield.cs` | 地形加物件、可活动范围、统一射线检测 |
| `Maps/SandboxMap.cs` | 试驾场地图 |
| `Vehicles/TankSpec.cs` | 车型参数 |
| `Vehicles/TankCatalog.cs` | 车型表（阶段 1 只有中坦） |
| `Vehicles/TankState.cs` | 坦克仿真状态 |
| `Vehicles/TankPose.cs` | 由地形推算的姿态、炮耳、炮口、瞄准换算 |
| `Vehicles/TankMotion.cs` | 行驶、坡度、碰撞、炮塔和炮管跟随 |
| `Combat/Shell.cs` | 炮弹状态 |
| `Combat/Ballistics.cs` | 弹道积分、落点预测 |
| `InputCmd.cs` | 每 tick 的输入 |
| `GameEvent.cs` | 仿真事件 |
| `Simulation.cs` | 一个 tick 的推进入口 |

**新建：`tests/Tank.Sim.Tests/`**：`Tank.Sim.Tests.csproj`、`TestWorld.cs`，以及每个模块一个 `*Tests.cs`。

**新建：`src/Tank.Client/`**

| 文件 | 职责 |
|---|---|
| `scenes/Sandbox.tscn` | 试驾场场景 |
| `scripts/World/SimConvert.cs` | `System.Numerics` 与 Godot 类型互转 |
| `scripts/World/Scenery.cs` | 天空、雾、太阳 |
| `scripts/World/TerrainMesh.cs` | 由高度图生成分块低多边形网格 |
| `scripts/World/PropMeshes.cs` | 岩石网格 |
| `scripts/Vehicles/TankModel.cs` | 程序化坦克模型（车体、炮塔、炮管节点分离） |
| `scripts/Cameras/OrbitCamera.cs` | 第三人称环绕镜头和狙击镜 |
| `scripts/Combat/ShellViews.cs` | 炮弹曳光 |
| `scripts/Combat/BattleFx.cs` | 炮口焰、爆炸 |
| `scripts/Overlay/Reticle.cs` | 准星、装填进度、状态文字（命名空间不叫 `Hud`，否则会遮蔽旧代码里的 `Tank.Client.UI.Hud` 类） |
| `scripts/Sandbox/SandboxInput.cs` | 注册按键动作并采样 |
| `scripts/Sandbox/SandboxMain.cs` | 试驾场主循环 |

**修改：** `Tank.sln`、`.github/workflows/ci.yml`、`src/Tank.Client/Tank.Client.csproj`、`src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs`、设计文档 §2.1 和 §10。

---

### Task 0：同步设计文档

**Files:**
- Modify: `docs/superpowers/specs/2026-10-13-third-person-tank-design.md`

- [ ] **Step 1: 改启动方式**

把 §2.1 中的这一行：

```
- 客户端新增 `scenes/Sandbox.tscn` 试驾场，用启动参数 `--sandbox` 进入。旧的主菜单和对局继续可用。
```

替换为：

```
- 客户端新增 `scenes/Sandbox.tscn` 试驾场，用 `godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn` 直接启动，不改动旧的主菜单。菜单入口在阶段 4 重写菜单时再加。
```

把 §12 表格阶段 1 一行中的 `` `--sandbox` 启动 `` 改为 `` 场景路径启动 ``。

- [ ] **Step 2: 改命名空间**

把 §10 代码块中的这一行：

```
  Math/        Obb2、Segment、Ray、XorShift、角度工具
```

替换为：

```
  Geometry/    Obb2、Intersect、XorShift、Angles（不叫 Math，避免遮蔽 System.Math）
```

同一处，§9 代码块中 `  Hud/        准星和散布圈…` 这一行开头的 `Hud/` 改为 `Overlay/`，并在该代码块下方加一句：「HUD 目录不叫 `Hud`，因为旧代码有 `Tank.Client.UI.Hud` 类，同名命名空间会遮蔽它。」

- [ ] **Step 3: 提交**

```bash
git add docs/superpowers/specs/2026-10-13-third-person-tank-design.md
git commit -m "docs: stage 1 launch path and Geometry namespace"
```

---

### Task 1：建立 Tank.Sim 项目，实现 Angles

**Files:**
- Create: `src/Tank.Sim/Tank.Sim.csproj`
- Create: `src/Tank.Sim/SimConstants.cs`
- Create: `src/Tank.Sim/Geometry/Angles.cs`
- Create: `tests/Tank.Sim.Tests/Tank.Sim.Tests.csproj`
- Create: `tests/Tank.Sim.Tests/AnglesTests.cs`
- Modify: `Tank.sln`

- [ ] **Step 1: 建项目**

`src/Tank.Sim/Tank.Sim.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- Third-person tank simulation. Must not reference Godot, networking or wall-clock time. -->
  <ItemGroup>
    <InternalsVisibleTo Include="Tank.Sim.Tests" />
  </ItemGroup>
</Project>
```

`tests/Tank.Sim.Tests/Tank.Sim.Tests.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Tank.Sim\Tank.Sim.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

`src/Tank.Sim/SimConstants.cs`：

```csharp
namespace Tank.Sim;

public static class SimConstants
{
    public const int TickRate = 30;
    public const float Dt = 1f / TickRate;
    public const float Gravity = 9.81f;
}
```

```bash
dotnet sln Tank.sln add src/Tank.Sim/Tank.Sim.csproj tests/Tank.Sim.Tests/Tank.Sim.Tests.csproj
```

- [ ] **Step 2: 写失败的测试**

`tests/Tank.Sim.Tests/AnglesTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;

namespace Tank.Sim.Tests;

public sealed class AnglesTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(2.5f * MathF.PI, 0.5f * MathF.PI)]
    [InlineData(-MathF.PI, MathF.PI)]
    [InlineData(-3.5f * MathF.PI, 0.5f * MathF.PI)]
    public void Wrap_MapsIntoMinusPiToPi(float input, float expected) =>
        Assert.Equal(expected, Angles.Wrap(input), 1e-4f);

    [Fact]
    public void MoveTowards_TakesTheShortArcAcrossPi()
    {
        float result = Angles.MoveTowards(Angles.Deg(170), Angles.Deg(-170), Angles.Deg(5));
        Assert.Equal(Angles.Deg(175), result, 1e-4f);
    }

    [Fact]
    public void MoveTowards_SnapsWhenWithinStep() =>
        Assert.Equal(0.3f, Angles.MoveTowards(0.25f, 0.3f, 0.1f), 1e-6f);

    [Fact]
    public void Lerp_InterpolatesAcrossPi() =>
        Assert.Equal(MathF.PI, MathF.Abs(Angles.Lerp(Angles.Deg(170), Angles.Deg(-170), 0.5f)), 1e-4f);

    [Fact]
    public void Forward_AtZeroYaw_IsMinusZ_AndPositiveYawTurnsLeft()
    {
        Assert.Equal(new Vector2(0, -1), Angles.Forward(0));
        Vector2 left = Angles.Forward(MathF.PI / 2);
        Assert.Equal(-1f, left.X, 1e-6f);
        Assert.Equal(0f, left.Y, 1e-6f);
    }

    [Fact]
    public void Right_IsPerpendicularToForward_OnTheRightHandSide()
    {
        Assert.Equal(new Vector2(1, 0), Angles.Right(0));
        Assert.Equal(0f, Vector2.Dot(Angles.Forward(0.7f), Angles.Right(0.7f)), 1e-6f);
    }

    [Fact]
    public void AimAngles_InvertsDirection()
    {
        Vector3 dir = Angles.Direction(1.1f, -0.2f);
        var (yaw, pitch) = Angles.AimAngles(dir * 37f);
        Assert.Equal(1.1f, yaw, 1e-5f);
        Assert.Equal(-0.2f, pitch, 1e-5f);
    }
}
```

- [ ] **Step 3: 运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: 编译失败，`The type or namespace name 'Geometry' does not exist`

- [ ] **Step 4: 实现**

`src/Tank.Sim/Geometry/Angles.cs`：

```csharp
using System.Numerics;

namespace Tank.Sim.Geometry;

/// <summary>
/// Angle helpers. Yaw 0 faces -Z and positive yaw turns left (counter-clockwise seen from above), same as Godot's
/// Rotation.Y. Pitch is positive upward. Ground vectors are (x, z) packed into <see cref="Vector2"/>.
/// </summary>
public static class Angles
{
    public const float Pi = MathF.PI;
    public const float TwoPi = MathF.PI * 2;

    public static float Deg(float degrees) => degrees * (Pi / 180f);

    /// <summary>Wraps to (-π, π].</summary>
    public static float Wrap(float a)
    {
        a %= TwoPi;
        if (a <= -Pi) a += TwoPi;
        else if (a > Pi) a -= TwoPi;
        return a;
    }

    /// <summary>Turns <paramref name="current"/> toward <paramref name="target"/> along the shorter arc by at most <paramref name="maxStep"/>.</summary>
    public static float MoveTowards(float current, float target, float maxStep)
    {
        float diff = Wrap(target - current);
        if (MathF.Abs(diff) <= maxStep) return Wrap(target);
        return Wrap(current + MathF.CopySign(maxStep, diff));
    }

    /// <summary>Interpolates along the shorter arc.</summary>
    public static float Lerp(float a, float b, float t) => Wrap(a + Wrap(b - a) * t);

    public static Vector2 Forward(float yaw) => new(-MathF.Sin(yaw), -MathF.Cos(yaw));

    public static Vector2 Right(float yaw) => new(MathF.Cos(yaw), -MathF.Sin(yaw));

    /// <summary>Unit 3D direction for a yaw and pitch.</summary>
    public static Vector3 Direction(float yaw, float pitch)
    {
        float c = MathF.Cos(pitch);
        return new Vector3(-MathF.Sin(yaw) * c, MathF.Sin(pitch), -MathF.Cos(yaw) * c);
    }

    /// <summary>Inverse of <see cref="Direction"/>; <paramref name="direction"/> need not be normalized.</summary>
    public static (float Yaw, float Pitch) AimAngles(Vector3 direction)
    {
        float length = direction.Length();
        if (length < 1e-6f) return (0, 0);
        return (MathF.Atan2(-direction.X, -direction.Z), MathF.Asin(System.Math.Clamp(direction.Y / length, -1f, 1f)));
    }
}
```

- [ ] **Step 5: 运行，确认通过**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: `Passed!  - Failed: 0, Passed: 10`

- [ ] **Step 6: 提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests Tank.sln
git commit -m "feat(sim): Tank.Sim project with angle helpers"
```

---

### Task 2：Obb2 与 Intersect

**Files:**
- Create: `src/Tank.Sim/Geometry/Obb2.cs`
- Create: `src/Tank.Sim/Geometry/Intersect.cs`
- Create: `tests/Tank.Sim.Tests/GeometryTests.cs`

- [ ] **Step 1: 写失败的测试**

`tests/Tank.Sim.Tests/GeometryTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;

namespace Tank.Sim.Tests;

public sealed class GeometryTests
{
    private static Obb2 Square(float x, float z, float yaw = 0) => new(new Vector2(x, z), new Vector2(1, 1), yaw);

    [Fact]
    public void Penetration_SeparatedBoxes_ReturnsFalse() =>
        Assert.False(Obb2.TryGetPenetration(Square(0, 0), Square(3, 0), out _));

    [Fact]
    public void Penetration_Overlap_PushesAlongShallowestAxis()
    {
        Assert.True(Obb2.TryGetPenetration(Square(0, 0), Square(1.5f, 0), out var push));
        Assert.Equal(-0.5f, push.X, 1e-5f);
        Assert.Equal(0f, push.Y, 1e-5f);
    }

    [Fact]
    public void Penetration_RotatedBox_UsesItsProjectedRadius()
    {
        Assert.True(Obb2.TryGetPenetration(Square(0, 0), Square(2.3f, 0, MathF.PI / 4), out var push));
        Assert.Equal(-(1 + MathF.Sqrt(2) - 2.3f), push.X, 1e-4f);
        Assert.Equal(0f, push.Y, 1e-4f);
    }

    [Fact]
    public void Contains_UsesLocalAxes()
    {
        var box = new Obb2(Vector2.Zero, new Vector2(1, 3), MathF.PI / 2); // long side now along X
        Assert.True(box.Contains(new Vector2(2.5f, 0)));
        Assert.False(box.Contains(new Vector2(0, 2.5f)));
    }

    [Fact]
    public void SegmentPrism_HitsTheNearFace()
    {
        Assert.True(Intersect.SegmentPrism(Square(0, 0), 0, 2, new Vector3(-5, 1, 0), new Vector3(5, 1, 0), out float t));
        Assert.Equal(0.4f, t, 1e-5f);
    }

    [Fact]
    public void SegmentPrism_PassingAboveTheTop_Misses() =>
        Assert.False(Intersect.SegmentPrism(Square(0, 0), 0, 2, new Vector3(-5, 3, 0), new Vector3(5, 3, 0), out _));

    [Fact]
    public void SegmentPrism_RotatedBox_HitsCorner()
    {
        Assert.True(Intersect.SegmentPrism(Square(0, 0, MathF.PI / 4), 0, 2, new Vector3(-5, 1, 0), new Vector3(5, 1, 0), out float t));
        Assert.Equal((5 - MathF.Sqrt(2)) / 10, t, 1e-4f);
    }

    [Fact]
    public void SegmentPrism_StartingInside_HitsAtZero()
    {
        Assert.True(Intersect.SegmentPrism(Square(0, 0), 0, 2, new Vector3(0, 1, 0), new Vector3(5, 1, 0), out float t));
        Assert.Equal(0f, t);
    }
}
```

- [ ] **Step 2: 运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: 编译失败，`The type or namespace name 'Obb2' could not be found`

- [ ] **Step 3: 实现**

`src/Tank.Sim/Geometry/Obb2.cs`：

```csharp
using System.Numerics;

namespace Tank.Sim.Geometry;

/// <summary>
/// Oriented rectangle on the ground. <see cref="HalfExtents"/>.X runs along <see cref="AxisRight"/> (width),
/// .Y along <see cref="AxisForward"/> (length). Center.Y is world Z.
/// </summary>
public readonly record struct Obb2(Vector2 Center, Vector2 HalfExtents, float Yaw)
{
    public Vector2 AxisRight => Angles.Right(Yaw);

    public Vector2 AxisForward => Angles.Forward(Yaw);

    public Obb2 WithCenter(Vector2 center) => this with { Center = center };

    /// <summary>Half the box's extent projected onto unit <paramref name="axis"/>.</summary>
    public float Radius(Vector2 axis) =>
        HalfExtents.X * MathF.Abs(Vector2.Dot(AxisRight, axis)) + HalfExtents.Y * MathF.Abs(Vector2.Dot(AxisForward, axis));

    public bool Contains(Vector2 point)
    {
        Vector2 d = point - Center;
        return MathF.Abs(Vector2.Dot(d, AxisRight)) <= HalfExtents.X && MathF.Abs(Vector2.Dot(d, AxisForward)) <= HalfExtents.Y;
    }

    /// <summary>Separating-axis test. On overlap, <paramref name="push"/> is the shortest move that takes <paramref name="a"/> out of <paramref name="b"/>.</summary>
    public static bool TryGetPenetration(in Obb2 a, in Obb2 b, out Vector2 push)
    {
        push = Vector2.Zero;
        float best = float.MaxValue;
        Vector2 d = a.Center - b.Center;
        Span<Vector2> axes = stackalloc Vector2[4] { a.AxisRight, a.AxisForward, b.AxisRight, b.AxisForward };
        foreach (Vector2 axis in axes)
        {
            float distance = Vector2.Dot(d, axis);
            float overlap = a.Radius(axis) + b.Radius(axis) - MathF.Abs(distance);
            if (overlap <= 0)
            {
                push = Vector2.Zero;
                return false;
            }
            if (overlap < best)
            {
                best = overlap;
                push = axis * (distance >= 0 ? overlap : -overlap);
            }
        }
        return true;
    }
}
```

`src/Tank.Sim/Geometry/Intersect.cs`：

```csharp
using System.Numerics;

namespace Tank.Sim.Geometry;

public static class Intersect
{
    /// <summary>
    /// Where segment <paramref name="from"/>→<paramref name="to"/> first enters <paramref name="box"/> extruded
    /// vertically between <paramref name="bottom"/> and <paramref name="top"/>; <paramref name="t"/> is in [0, 1].
    /// </summary>
    public static bool SegmentPrism(in Obb2 box, float bottom, float top, Vector3 from, Vector3 to, out float t)
    {
        t = 0;
        Vector2 offset = new Vector2(from.X, from.Z) - box.Center;
        Vector2 delta = new(to.X - from.X, to.Z - from.Z);
        Vector2 right = box.AxisRight, forward = box.AxisForward;
        float tMin = 0f, tMax = 1f;
        if (!Slab(Vector2.Dot(offset, right), Vector2.Dot(delta, right), -box.HalfExtents.X, box.HalfExtents.X, ref tMin, ref tMax)) return false;
        if (!Slab(Vector2.Dot(offset, forward), Vector2.Dot(delta, forward), -box.HalfExtents.Y, box.HalfExtents.Y, ref tMin, ref tMax)) return false;
        if (!Slab(from.Y, to.Y - from.Y, bottom, top, ref tMin, ref tMax)) return false;
        t = tMin;
        return true;
    }

    private static bool Slab(float start, float delta, float min, float max, ref float tMin, ref float tMax)
    {
        if (MathF.Abs(delta) < 1e-8f) return start >= min && start <= max;
        float t0 = (min - start) / delta, t1 = (max - start) / delta;
        if (t0 > t1) (t0, t1) = (t1, t0);
        tMin = MathF.Max(tMin, t0);
        tMax = MathF.Min(tMax, t1);
        return tMin <= tMax;
    }
}
```

- [ ] **Step 4: 运行，确认通过**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: `Failed: 0, Passed: 18`

- [ ] **Step 5: 提交**

```bash
git add src/Tank.Sim/Geometry tests/Tank.Sim.Tests/GeometryTests.cs
git commit -m "feat(sim): oriented boxes and segment-prism intersection"
```

---

### Task 3：HeightField

**Files:**
- Create: `src/Tank.Sim/Terrain/HeightField.cs`
- Create: `tests/Tank.Sim.Tests/TestWorld.cs`
- Create: `tests/Tank.Sim.Tests/HeightFieldTests.cs`

- [ ] **Step 1: 测试辅助**

`tests/Tank.Sim.Tests/TestWorld.cs`（Task 7 会扩充）：

```csharp
using Tank.Sim.Terrain;

namespace Tank.Sim.Tests;

internal static class TestWorld
{
    public const int Samples = 101;
    public const float Cell = 2f; // 200m × 200m
    public const float Size = (Samples - 1) * Cell;
    public const float Border = 4f;

    public static HeightField Flat(float height = 0f) =>
        new(Samples, Samples, Cell, Enumerable.Repeat(height, Samples * Samples).ToArray());

    /// <summary>Ground rising toward -Z (north) by <paramref name="grade"/> metres per metre.</summary>
    public static HeightField RampNorth(float grade)
    {
        var heights = new float[Samples * Samples];
        for (int j = 0; j < Samples; j++)
        for (int i = 0; i < Samples; i++)
            heights[j * Samples + i] = (Samples - 1 - j) * Cell * grade;
        return new HeightField(Samples, Samples, Cell, heights);
    }
}
```

- [ ] **Step 2: 写失败的测试**

`tests/Tank.Sim.Tests/HeightFieldTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Terrain;

namespace Tank.Sim.Tests;

public sealed class HeightFieldTests
{
    // 3×3 samples, 10m apart: row j=0 is 0,1,2; j=1 is 3,4,5; j=2 is 6,7,8.
    private static readonly HeightField Small = new(3, 3, 10f, new float[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 });

    [Fact]
    public void HeightAt_OnSamples_ReturnsSampleValue() => Assert.Equal(4f, Small.HeightAt(10, 10), 1e-5f);

    [Fact]
    public void HeightAt_BetweenSamples_IsBilinear()
    {
        Assert.Equal(0.5f, Small.HeightAt(5, 0), 1e-5f);
        Assert.Equal(2f, Small.HeightAt(5, 5), 1e-5f);
    }

    [Fact]
    public void HeightAt_OutsideGrid_UsesEdge()
    {
        Assert.Equal(0f, Small.HeightAt(-50, 0), 1e-5f);
        Assert.Equal(8f, Small.HeightAt(100, 100), 1e-5f);
    }

    [Fact]
    public void Size_IsCellsTimesCellSize() => Assert.Equal(20f, Small.SizeX);

    [Fact]
    public void NormalAt_FlatGround_PointsUp()
    {
        Vector3 n = TestWorld.Flat(3).NormalAt(50, 50);
        Assert.Equal(1f, n.Y, 1e-5f);
    }

    [Fact]
    public void NormalAt_Ramp_TiltsAwayFromHighSide()
    {
        Vector3 n = TestWorld.RampNorth(1f).NormalAt(50, 50);
        Assert.Equal(MathF.Sqrt(0.5f), n.Y, 1e-4f);
        Assert.Equal(MathF.Sqrt(0.5f), n.Z, 1e-4f);
    }

    [Fact]
    public void Raycast_DownIntoFlatGround_HitsAtSurface()
    {
        Assert.True(TestWorld.Flat().Raycast(new Vector3(10, 10, 10), new Vector3(10, -10, 10), out float t));
        Assert.Equal(0.5f, t, 1e-3f);
    }

    [Fact]
    public void Raycast_AboveGround_Misses() =>
        Assert.False(TestWorld.Flat().Raycast(new Vector3(0, 5, 0), new Vector3(20, 5, 20), out _));

    [Fact]
    public void Ctor_RejectsWrongHeightCount() =>
        Assert.Throws<ArgumentException>(() => new HeightField(3, 3, 1f, new float[8]));
}
```

- [ ] **Step 3: 运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: 编译失败，`The type or namespace name 'Terrain' does not exist`

- [ ] **Step 4: 实现**

`src/Tank.Sim/Terrain/HeightField.cs`：

```csharp
using System.Numerics;

namespace Tank.Sim.Terrain;

/// <summary>
/// Regular grid of ground heights. Sample (i, j) sits at world (i·CellSize, j·CellSize): X grows with i, Z with j.
/// The surface between samples is bilinear; outside the grid the edge height continues.
/// </summary>
public sealed class HeightField
{
    private const int RefineSteps = 12;
    private readonly float[] _heights;

    public HeightField(int samplesX, int samplesZ, float cellSize, float[] heights)
    {
        if (samplesX < 2 || samplesZ < 2) throw new ArgumentOutOfRangeException(nameof(samplesX), "Need at least 2×2 samples.");
        if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be positive.");
        if (heights.Length != samplesX * samplesZ)
            throw new ArgumentException($"Expected {samplesX * samplesZ} heights, got {heights.Length}.", nameof(heights));
        SamplesX = samplesX;
        SamplesZ = samplesZ;
        CellSize = cellSize;
        _heights = heights;
    }

    public int SamplesX { get; }
    public int SamplesZ { get; }
    public float CellSize { get; }
    public float SizeX => (SamplesX - 1) * CellSize;
    public float SizeZ => (SamplesZ - 1) * CellSize;

    public float Sample(int i, int j) =>
        _heights[System.Math.Clamp(j, 0, SamplesZ - 1) * SamplesX + System.Math.Clamp(i, 0, SamplesX - 1)];

    public float HeightAt(float x, float z)
    {
        float fx = System.Math.Clamp(x / CellSize, 0, SamplesX - 1);
        float fz = System.Math.Clamp(z / CellSize, 0, SamplesZ - 1);
        int i = System.Math.Min((int)fx, SamplesX - 2);
        int j = System.Math.Min((int)fz, SamplesZ - 2);
        float u = fx - i, v = fz - j;
        float near = Sample(i, j) * (1 - u) + Sample(i + 1, j) * u;
        float far = Sample(i, j + 1) * (1 - u) + Sample(i + 1, j + 1) * u;
        return near * (1 - v) + far * v;
    }

    public Vector3 NormalAt(float x, float z)
    {
        float e = CellSize * 0.5f;
        float dx = HeightAt(x + e, z) - HeightAt(x - e, z);
        float dz = HeightAt(x, z + e) - HeightAt(x, z - e);
        return Vector3.Normalize(new Vector3(-dx, 2 * e, -dz));
    }

    /// <summary>First point where segment <paramref name="from"/>→<paramref name="to"/> goes below ground, as t in [0, 1].</summary>
    public bool Raycast(Vector3 from, Vector3 to, out float t)
    {
        t = 0;
        if (Below(from)) return true;
        int steps = System.Math.Max(1, (int)MathF.Ceiling(Vector3.Distance(from, to) / (CellSize * 0.5f)));
        float previous = 0;
        for (int s = 1; s <= steps; s++)
        {
            float current = (float)s / steps;
            if (Below(Vector3.Lerp(from, to, current)))
            {
                t = Refine(from, to, previous, current);
                return true;
            }
            previous = current;
        }
        return false;
    }

    private bool Below(Vector3 p) => p.Y < HeightAt(p.X, p.Z);

    private float Refine(Vector3 from, Vector3 to, float above, float below)
    {
        for (int k = 0; k < RefineSteps; k++)
        {
            float mid = (above + below) * 0.5f;
            if (Below(Vector3.Lerp(from, to, mid))) below = mid;
            else above = mid;
        }
        return below;
    }
}
```

- [ ] **Step 5: 运行，确认通过**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: `Failed: 0, Passed: 27`

- [ ] **Step 6: 提交**

```bash
git add src/Tank.Sim/Terrain tests/Tank.Sim.Tests/TestWorld.cs tests/Tank.Sim.Tests/HeightFieldTests.cs
git commit -m "feat(sim): height field sampling and ray marching"
```

---

### Task 4：XorShift 与 TerrainGenerator

**Files:**
- Create: `src/Tank.Sim/Geometry/XorShift.cs`
- Create: `src/Tank.Sim/Terrain/TerrainGenerator.cs`
- Create: `tests/Tank.Sim.Tests/TerrainGeneratorTests.cs`

- [ ] **Step 1: 写失败的测试**

`tests/Tank.Sim.Tests/TerrainGeneratorTests.cs`：

```csharp
using Tank.Sim.Geometry;
using Tank.Sim.Terrain;

namespace Tank.Sim.Tests;

public sealed class TerrainGeneratorTests
{
    private static readonly TerrainGenerator.Hill[] NoHills = Array.Empty<TerrainGenerator.Hill>();

    [Fact]
    public void SameSeed_GivesIdenticalTerrain()
    {
        var a = TerrainGenerator.Generate(7, 33, 2f, 5f, NoHills);
        var b = TerrainGenerator.Generate(7, 33, 2f, 5f, NoHills);
        for (int j = 0; j < 33; j++)
        for (int i = 0; i < 33; i++)
            Assert.Equal(a.Sample(i, j), b.Sample(i, j));
    }

    [Fact]
    public void DifferentSeed_GivesDifferentTerrain()
    {
        var a = TerrainGenerator.Generate(7, 33, 2f, 5f, NoHills);
        var b = TerrainGenerator.Generate(8, 33, 2f, 5f, NoHills);
        Assert.Contains(Enumerable.Range(0, 33), i => a.Sample(i, 16) != b.Sample(i, 16));
    }

    [Fact]
    public void Noise_StaysWithinRoughness()
    {
        var field = TerrainGenerator.Generate(3, 65, 2f, 5f, NoHills);
        for (int j = 0; j < 65; j++)
        for (int i = 0; i < 65; i++)
            Assert.InRange(field.Sample(i, j), 0f, 5f);
    }

    [Fact]
    public void Hill_PeaksAtItsCentre_AndFadesToZeroAtItsRadius()
    {
        var field = TerrainGenerator.Generate(1, 101, 2f, 0f, new[] { new TerrainGenerator.Hill(100, 100, 30, 10) });
        Assert.Equal(10f, field.HeightAt(100, 100), 1e-4f);
        Assert.Equal(0f, field.HeightAt(100, 132), 1e-4f);
    }

    [Fact]
    public void XorShift_SameSeed_SameSequence_InUnitRange()
    {
        var a = new XorShift(42);
        var b = new XorShift(42);
        for (int k = 0; k < 100; k++)
        {
            float x = a.NextFloat();
            Assert.Equal(x, b.NextFloat());
            Assert.InRange(x, 0f, 0.99999994f);
        }
    }
}
```

- [ ] **Step 2: 运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: 编译失败，`'TerrainGenerator' could not be found`

- [ ] **Step 3: 实现**

`src/Tank.Sim/Geometry/XorShift.cs`：

```csharp
namespace Tank.Sim.Geometry;

/// <summary>Tiny seeded generator (xorshift32). Identical sequence on every platform.</summary>
public struct XorShift
{
    private uint _state;

    public XorShift(uint seed) => _state = seed == 0 ? 0x9E3779B9u : seed;

    public uint NextUInt()
    {
        uint x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }

    /// <summary>Uniform in [0, 1).</summary>
    public float NextFloat() => (NextUInt() >> 8) / 16777216f;
}
```

`src/Tank.Sim/Terrain/TerrainGenerator.cs`：

```csharp
namespace Tank.Sim.Terrain;

/// <summary>Seeded fractal value noise plus round hills. Same inputs give the same terrain on every machine.</summary>
public static class TerrainGenerator
{
    private const float NoiseScale = 60f;
    private const int Octaves = 4;

    /// <summary>A cosine-shaped bump: full <see cref="Height"/> at the centre, zero at <see cref="Radius"/>.</summary>
    public readonly record struct Hill(float X, float Z, float Radius, float Height);

    public static HeightField Generate(int seed, int samples, float cellSize, float roughness, IReadOnlyList<Hill> hills)
    {
        var heights = new float[samples * samples];
        for (int j = 0; j < samples; j++)
        for (int i = 0; i < samples; i++)
        {
            float x = i * cellSize, z = j * cellSize;
            float h = Fractal(seed, x / NoiseScale, z / NoiseScale) * roughness;
            for (int k = 0; k < hills.Count; k++) h += HillHeight(hills[k], x, z);
            heights[j * samples + i] = h;
        }
        return new HeightField(samples, samples, cellSize, heights);
    }

    private static float HillHeight(in Hill hill, float x, float z)
    {
        float dx = x - hill.X, dz = z - hill.Z;
        float d = MathF.Sqrt(dx * dx + dz * dz) / hill.Radius;
        return d >= 1 ? 0 : hill.Height * 0.5f * (1 + MathF.Cos(d * MathF.PI));
    }

    private static float Fractal(int seed, float x, float z)
    {
        float sum = 0, amplitude = 1, norm = 0;
        for (int octave = 0; octave < Octaves; octave++)
        {
            sum += Value(seed + octave * 1013, x, z) * amplitude;
            norm += amplitude;
            amplitude *= 0.5f;
            x *= 2;
            z *= 2;
        }
        return sum / norm;
    }

    private static float Value(int seed, float x, float z)
    {
        int xi = (int)MathF.Floor(x), zi = (int)MathF.Floor(z);
        float u = Smooth(x - xi), v = Smooth(z - zi);
        float near = Hash(seed, xi, zi) * (1 - u) + Hash(seed, xi + 1, zi) * u;
        float far = Hash(seed, xi, zi + 1) * (1 - u) + Hash(seed, xi + 1, zi + 1) * u;
        return near * (1 - v) + far * v;
    }

    private static float Smooth(float t) => t * t * (3 - 2 * t);

    /// <summary>Integer hash to [0, 1).</summary>
    private static float Hash(int seed, int x, int z)
    {
        unchecked
        {
            uint h = (uint)seed * 374761393u + (uint)x * 668265263u + (uint)z * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
}
```

- [ ] **Step 4: 运行，确认通过**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: `Failed: 0, Passed: 32`

- [ ] **Step 5: 提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests/TerrainGeneratorTests.cs
git commit -m "feat(sim): seeded terrain generator"
```

---

### Task 5：Prop 与 Battlefield

**Files:**
- Create: `src/Tank.Sim/Maps/Prop.cs`
- Create: `src/Tank.Sim/Maps/Battlefield.cs`
- Create: `tests/Tank.Sim.Tests/BattlefieldTests.cs`

- [ ] **Step 1: 写失败的测试**

`tests/Tank.Sim.Tests/BattlefieldTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Sim.Tests;

public sealed class BattlefieldTests
{
    // A 2m-thick slab across the X axis at x = 59..61, 10m tall.
    private static readonly Prop Slab = new(1, PropKind.Rock, new Obb2(new Vector2(60, 100), new Vector2(1, 5), 0), -1, 10);

    private static Battlefield WithSlab() => new(TestWorld.Flat(), new[] { Slab }, TestWorld.Border);

    [Fact]
    public void Raycast_ReturnsPropWhenItIsNearer()
    {
        Assert.True(WithSlab().Raycast(new Vector3(20, 1, 100), new Vector3(120, 1, 100), out var hit));
        Assert.Equal(Slab.Id, hit.PropId);
        Assert.Equal(59f, hit.Point.X, 1e-3f);
    }

    [Fact]
    public void Raycast_GroundHit_ReportsTerrain()
    {
        Assert.True(WithSlab().Raycast(new Vector3(20, 5, 100), new Vector3(20, -5, 100), out var hit));
        Assert.Equal(RayHit.TerrainId, hit.PropId);
        Assert.Equal(0f, hit.Point.Y, 1e-3f);
    }

    [Fact]
    public void Raycast_ClearPath_Misses() =>
        Assert.False(WithSlab().Raycast(new Vector3(20, 5, 100), new Vector3(40, 5, 100), out _));

    [Fact]
    public void ClampToPlayable_KeepsPointsInsideTheBorder()
    {
        Vector2 p = WithSlab().ClampToPlayable(new Vector2(-10, 300));
        Assert.Equal(new Vector2(TestWorld.Border, TestWorld.Size - TestWorld.Border), p);
    }

    [Fact]
    public void Ctor_RejectsBorderWiderThanHalfTheMap() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Battlefield(TestWorld.Flat(), Array.Empty<Prop>(), 120));
}
```

- [ ] **Step 2: 运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: 编译失败，`The type or namespace name 'Maps' does not exist`

- [ ] **Step 3: 实现**

`src/Tank.Sim/Maps/Prop.cs`：

```csharp
using Tank.Sim.Geometry;

namespace Tank.Sim.Maps;

public enum PropKind : byte
{
    Rock = 0,
}

/// <summary>A static object: a ground footprint extruded from <see cref="Bottom"/> up by <see cref="Height"/>.</summary>
public readonly record struct Prop(int Id, PropKind Kind, Obb2 Footprint, float Bottom, float Height)
{
    public float Top => Bottom + Height;
}
```

`src/Tank.Sim/Maps/Battlefield.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Terrain;

namespace Tank.Sim.Maps;

/// <summary>Where a segment test stopped. <see cref="PropId"/> is <see cref="TerrainId"/> for the ground.</summary>
public readonly record struct RayHit(float T, Vector3 Point, int PropId)
{
    public const int TerrainId = 0;
}

/// <summary>Terrain plus static props, and the rectangle tanks may drive in.</summary>
public sealed class Battlefield
{
    private readonly Prop[] _props;

    public Battlefield(HeightField terrain, IEnumerable<Prop> props, float border)
    {
        if (border < 0 || border * 2 >= MathF.Min(terrain.SizeX, terrain.SizeZ))
            throw new ArgumentOutOfRangeException(nameof(border), $"Border {border}m leaves no playable area.");
        Terrain = terrain;
        _props = props.ToArray();
        Border = border;
    }

    public HeightField Terrain { get; }
    public IReadOnlyList<Prop> Props => _props;
    public float Border { get; }

    public Vector2 ClampToPlayable(Vector2 p) =>
        Vector2.Clamp(p, new Vector2(Border, Border), new Vector2(Terrain.SizeX - Border, Terrain.SizeZ - Border));

    /// <summary>Nearest hit of segment <paramref name="from"/>→<paramref name="to"/> against ground and props.</summary>
    public bool Raycast(Vector3 from, Vector3 to, out RayHit hit)
    {
        bool found = Terrain.Raycast(from, to, out float best);
        int propId = RayHit.TerrainId;
        foreach (var prop in _props)
        {
            if (Intersect.SegmentPrism(prop.Footprint, prop.Bottom, prop.Top, from, to, out float t) && (!found || t < best))
            {
                found = true;
                best = t;
                propId = prop.Id;
            }
        }
        hit = found ? new RayHit(best, Vector3.Lerp(from, to, best), propId) : default;
        return found;
    }
}
```

- [ ] **Step 4: 运行，确认通过**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: `Failed: 0, Passed: 37`

- [ ] **Step 5: 提交**

```bash
git add src/Tank.Sim/Maps tests/Tank.Sim.Tests/BattlefieldTests.cs
git commit -m "feat(sim): battlefield with props and combined raycast"
```

---

### Task 6：车型参数、坦克状态与姿态

**Files:**
- Create: `src/Tank.Sim/Vehicles/TankSpec.cs`
- Create: `src/Tank.Sim/Vehicles/TankCatalog.cs`
- Create: `src/Tank.Sim/Vehicles/TankState.cs`
- Create: `src/Tank.Sim/Vehicles/TankPose.cs`
- Create: `tests/Tank.Sim.Tests/TankPoseTests.cs`

- [ ] **Step 1: 写失败的测试**

`tests/Tank.Sim.Tests/TankPoseTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class TankPoseTests
{
    private static readonly TankSpec Spec = TankCatalog.Medium;

    private static TankPose PoseOn(HeightField terrain, float yaw) =>
        TankPose.Compute(new TankState { Id = 1, Spec = Spec, Position = new Vector2(100, 100), Yaw = yaw }, terrain);

    [Fact]
    public void Catalog_AllSpecsHavePositiveValues()
    {
        foreach (var s in TankCatalog.All)
        {
            Assert.All(new[]
            {
                s.HullLength, s.HullWidth, s.MaxForwardSpeed, s.MaxReverseSpeed, s.Acceleration, s.Braking, s.HullTraverse,
                s.TurretTraverse, s.GunPitchSpeed, s.GunDepression, s.GunElevation, s.MaxClimb, s.TrunnionHeight, s.GunLength,
                s.ShellSpeed, s.ReloadTime,
            }, v => Assert.True(v > 0, $"{s.Id} has a non-positive parameter"));
        }
    }

    [Fact]
    public void FlatGround_SitsOnSurface_FacingYaw()
    {
        TankPose pose = PoseOn(TestWorld.Flat(5), 0.7f);
        Assert.Equal(new Vector3(100, 5, 100), pose.Position);
        Assert.Equal(-MathF.Sin(0.7f), pose.Forward.X, 1e-5f);
        Assert.Equal(0f, pose.Forward.Y, 1e-5f);
        Assert.Equal(-MathF.Cos(0.7f), pose.Forward.Z, 1e-5f);
    }

    [Fact]
    public void FacingUpARamp_PitchesNoseUp() =>
        Assert.Equal(MathF.Sin(MathF.Atan(0.3f)), PoseOn(TestWorld.RampNorth(0.3f), 0).Forward.Y, 1e-3f);

    [Fact]
    public void SideOnToARamp_RollsTowardTheLowSide()
    {
        TankPose pose = PoseOn(TestWorld.RampNorth(0.3f), -MathF.PI / 2); // facing +X, high ground on the left
        Assert.Equal(0f, pose.Forward.Y, 1e-3f);
        Assert.True(pose.Up.Z > 0.2f, $"Up.Z was {pose.Up.Z}");
    }

    [Fact]
    public void Trunnion_SitsAtSpecHeight_AheadOfCentreByTurretOffset()
    {
        Vector3 trunnion = PoseOn(TestWorld.Flat(), 0).Trunnion(Spec);
        Assert.Equal(100f, trunnion.X, 1e-5f);
        Assert.Equal(Spec.TrunnionHeight, trunnion.Y, 1e-5f);
        Assert.Equal(100f - Spec.TurretOffset, trunnion.Z, 1e-5f);
    }

    [Fact]
    public void ToHullAngles_InvertsGunDirection()
    {
        TankPose pose = PoseOn(TestWorld.RampNorth(0.3f), 0.3f);
        var (turretYaw, gunPitch) = pose.ToHullAngles(pose.GunDirection(0.5f, 0.1f));
        Assert.Equal(0.5f, turretYaw, 1e-4f);
        Assert.Equal(0.1f, gunPitch, 1e-4f);
    }
}
```

- [ ] **Step 2: 运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: 编译失败，`The type or namespace name 'Vehicles' does not exist`

- [ ] **Step 3: 实现**

`src/Tank.Sim/Vehicles/TankSpec.cs`：

```csharp
namespace Tank.Sim.Vehicles;

/// <summary>Per-vehicle constants. Metres, seconds, radians, m/s. Hull origin is the ground contact centre.</summary>
public sealed record TankSpec
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required float HullLength { get; init; }
    public required float HullWidth { get; init; }
    public required float MaxForwardSpeed { get; init; }
    public required float MaxReverseSpeed { get; init; }
    public required float Acceleration { get; init; }
    public required float Braking { get; init; }
    public required float HullTraverse { get; init; }
    public required float TurretTraverse { get; init; }
    public required float GunPitchSpeed { get; init; }

    /// <summary>How far the gun can point below the hull plane (positive number).</summary>
    public required float GunDepression { get; init; }

    public required float GunElevation { get; init; }
    public required float MaxClimb { get; init; }

    /// <summary>Gun pivot height above the hull origin.</summary>
    public required float TrunnionHeight { get; init; }

    /// <summary>Turret ring centre ahead (+) of the hull centre.</summary>
    public required float TurretOffset { get; init; }

    public required float GunLength { get; init; }
    public required float ShellSpeed { get; init; }
    public required float ReloadTime { get; init; }
}
```

`src/Tank.Sim/Vehicles/TankCatalog.cs`：

```csharp
using Tank.Sim.Geometry;

namespace Tank.Sim.Vehicles;

/// <summary>Vehicle table (design §4.4). Stage 1 ships the medium tank only.</summary>
public static class TankCatalog
{
    private const float Kmh = 1f / 3.6f;

    public static readonly TankSpec Medium = new()
    {
        Id = "medium",
        Name = "中型坦克",
        HullLength = 6.5f,
        HullWidth = 3.2f,
        MaxForwardSpeed = 50 * Kmh,
        MaxReverseSpeed = 18 * Kmh,
        Acceleration = 4f,
        Braking = 9f,
        HullTraverse = Angles.Deg(42),
        TurretTraverse = Angles.Deg(40),
        GunPitchSpeed = Angles.Deg(30),
        GunDepression = Angles.Deg(7),
        GunElevation = Angles.Deg(20),
        MaxClimb = Angles.Deg(32),
        TrunnionHeight = 2.1f,
        TurretOffset = 0.3f,
        GunLength = 4.2f,
        ShellSpeed = 780f,
        ReloadTime = 6.5f,
    };

    public static IReadOnlyList<TankSpec> All { get; } = new[] { Medium };
}
```

`src/Tank.Sim/Vehicles/TankState.cs`：

```csharp
using System.Numerics;

namespace Tank.Sim.Vehicles;

/// <summary>Everything that persists between ticks for one tank. Height and tilt are derived (see <see cref="TankPose"/>).</summary>
public struct TankState
{
    public int Id;
    public TankSpec Spec;

    /// <summary>Ground position: X is world X, Y is world Z.</summary>
    public Vector2 Position;

    public float Yaw;

    /// <summary>Signed speed along the hull's forward axis, m/s.</summary>
    public float Speed;

    /// <summary>Turret angle relative to the hull.</summary>
    public float TurretYaw;

    /// <summary>Gun angle relative to the hull plane.</summary>
    public float GunPitch;

    /// <summary>Seconds until the gun can fire.</summary>
    public float Reload;

    public int ShotCount;
}
```

`src/Tank.Sim/Vehicles/TankPose.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Terrain;

namespace Tank.Sim.Vehicles;

/// <summary>World placement of a hull resting on the terrain, recomputed from <see cref="TankState"/> whenever needed.</summary>
public readonly record struct TankPose(Vector3 Position, Quaternion Rotation)
{
    /// <summary>Fraction of the hull's half-size where the ground is sampled; inside the tracks so edges don't dig in.</summary>
    private const float ContactSpread = 0.8f;

    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Rotation);

    public Vector3 Up => Vector3.Transform(Vector3.UnitY, Rotation);

    public static TankPose Compute(in TankState t, HeightField terrain)
    {
        TankSpec spec = t.Spec;
        Vector2 center = t.Position;
        Vector2 f = Angles.Forward(t.Yaw) * (spec.HullLength * 0.5f * ContactSpread);
        Vector2 r = Angles.Right(t.Yaw) * (spec.HullWidth * 0.5f * ContactSpread);
        float frontRight = Height(terrain, center + f + r), frontLeft = Height(terrain, center + f - r);
        float backRight = Height(terrain, center - f + r), backLeft = Height(terrain, center - f - r);
        float front = (frontRight + frontLeft) * 0.5f, back = (backRight + backLeft) * 0.5f;
        float right = (frontRight + backRight) * 0.5f, left = (frontLeft + backLeft) * 0.5f;
        float pitch = MathF.Atan2(front - back, f.Length() * 2);
        float roll = MathF.Atan2(right - left, r.Length() * 2);
        float y = MathF.Max((front + back) * 0.5f, Height(terrain, center)); // don't sink into crests
        return new TankPose(new Vector3(center.X, y, center.Y), Quaternion.CreateFromYawPitchRoll(t.Yaw, pitch, roll));
    }

    /// <summary>Gun pivot in world space.</summary>
    public Vector3 Trunnion(TankSpec spec) =>
        Position + Vector3.Transform(new Vector3(0, spec.TrunnionHeight, -spec.TurretOffset), Rotation);

    public Vector3 GunDirection(float turretYaw, float gunPitch) =>
        Vector3.Transform(Angles.Direction(turretYaw, gunPitch), Rotation);

    public Vector3 Muzzle(TankSpec spec, float turretYaw, float gunPitch) =>
        Trunnion(spec) + GunDirection(turretYaw, gunPitch) * spec.GunLength;

    /// <summary>Turret yaw and gun pitch (hull-relative) that point the barrel along world <paramref name="direction"/>.</summary>
    public (float TurretYaw, float GunPitch) ToHullAngles(Vector3 direction) =>
        Angles.AimAngles(Vector3.Transform(direction, Quaternion.Inverse(Rotation)));

    private static float Height(HeightField terrain, Vector2 p) => terrain.HeightAt(p.X, p.Y);
}
```

- [ ] **Step 4: 运行，确认通过**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: `Failed: 0, Passed: 43`

- [ ] **Step 5: 提交**

```bash
git add src/Tank.Sim/Vehicles tests/Tank.Sim.Tests/TankPoseTests.cs
git commit -m "feat(sim): tank spec, state and terrain-fitted pose"
```

---

### Task 7：行驶（速度、转向、坡度）

**Files:**
- Create: `src/Tank.Sim/InputCmd.cs`
- Create: `src/Tank.Sim/Vehicles/TankMotion.cs`
- Modify: `tests/Tank.Sim.Tests/TestWorld.cs`（整文件替换）
- Create: `tests/Tank.Sim.Tests/TankMotionTests.cs`

- [ ] **Step 1: 扩充测试辅助**

把 `tests/Tank.Sim.Tests/TestWorld.cs` 整个替换为：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

internal static class TestWorld
{
    public const int Samples = 101;
    public const float Cell = 2f; // 200m × 200m
    public const float Size = (Samples - 1) * Cell;
    public const float Border = 4f;

    public static HeightField Flat(float height = 0f) =>
        new(Samples, Samples, Cell, Enumerable.Repeat(height, Samples * Samples).ToArray());

    /// <summary>Ground rising toward -Z (north) by <paramref name="grade"/> metres per metre.</summary>
    public static HeightField RampNorth(float grade)
    {
        var heights = new float[Samples * Samples];
        for (int j = 0; j < Samples; j++)
        for (int i = 0; i < Samples; i++)
            heights[j * Samples + i] = (Samples - 1 - j) * Cell * grade;
        return new HeightField(Samples, Samples, Cell, heights);
    }

    public static Battlefield Field(HeightField terrain, params Prop[] props) => new(terrain, props, Border);

    public static Battlefield FlatField(params Prop[] props) => Field(Flat(), props);

    public static TankState Tank(float x, float z, float yaw = 0f) =>
        new() { Id = 1, Spec = TankCatalog.Medium, Position = new Vector2(x, z), Yaw = yaw };

    public static InputCmd Cmd(sbyte throttle = 0, sbyte steer = 0, float aimYaw = 0f, float aimPitch = 0f, bool fire = false) =>
        new(0, throttle, steer, aimYaw, aimPitch, fire);

    public static void Drive(ref TankState t, Battlefield field, InputCmd cmd, int ticks, params Obb2[] obstacles)
    {
        for (int i = 0; i < ticks; i++) TankMotion.Drive(ref t, cmd, field, obstacles);
    }
}
```

- [ ] **Step 2: 写失败的测试**

`tests/Tank.Sim.Tests/TankMotionTests.cs`：

```csharp
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class TankMotionTests
{
    private static readonly TankSpec Spec = TankCatalog.Medium;

    [Fact]
    public void Throttle_OnFlatGround_AcceleratesAlongMinusZ()
    {
        var t = TestWorld.Tank(100, 100);
        TestWorld.Drive(ref t, TestWorld.FlatField(), TestWorld.Cmd(throttle: 1), SimConstants.TickRate);
        Assert.Equal(Spec.Acceleration, t.Speed, 0.01f);
        Assert.True(t.Position.Y < 100);
        Assert.Equal(100f, t.Position.X, 1e-3f);
    }

    [Fact]
    public void Throttle_HeldLong_CapsAtMaxForwardSpeed()
    {
        var t = TestWorld.Tank(100, 180);
        TestWorld.Drive(ref t, TestWorld.FlatField(), TestWorld.Cmd(throttle: 1), 300);
        Assert.Equal(Spec.MaxForwardSpeed, t.Speed, 1e-3f);
    }

    [Fact]
    public void Reverse_CapsAtMaxReverseSpeed_AndMovesBackward()
    {
        var t = TestWorld.Tank(100, 100);
        TestWorld.Drive(ref t, TestWorld.FlatField(), TestWorld.Cmd(throttle: -1), 300);
        Assert.Equal(-Spec.MaxReverseSpeed, t.Speed, 1e-3f);
        Assert.True(t.Position.Y > 100);
    }

    [Fact]
    public void ReleasingThrottle_BrakesToAStop()
    {
        var field = TestWorld.FlatField();
        var t = TestWorld.Tank(100, 180);
        TestWorld.Drive(ref t, field, TestWorld.Cmd(throttle: 1), 300);
        TestWorld.Drive(ref t, field, TestWorld.Cmd(), 60);
        Assert.Equal(0f, t.Speed);
    }

    [Fact]
    public void SteerRight_AtRest_TurnsClockwiseAtFullTraverse()
    {
        var t = TestWorld.Tank(100, 100);
        TestWorld.Drive(ref t, TestWorld.FlatField(), TestWorld.Cmd(steer: 1), SimConstants.TickRate);
        Assert.Equal(-Spec.HullTraverse, t.Yaw, 1e-3f);
    }

    [Fact]
    public void SteerWhileMoving_TurnsSlower()
    {
        var field = TestWorld.FlatField();
        var t = TestWorld.Tank(100, 180);
        TestWorld.Drive(ref t, field, TestWorld.Cmd(throttle: 1), 90);
        float before = t.Yaw;
        TestWorld.Drive(ref t, field, TestWorld.Cmd(throttle: 1, steer: 1), SimConstants.TickRate);
        Assert.Equal(-Spec.HullTraverse * 0.75f, t.Yaw - before, 1e-3f);
    }

    [Fact]
    public void GentleSlope_ReducesTopSpeed()
    {
        var t = TestWorld.Tank(100, 180);
        TestWorld.Drive(ref t, TestWorld.Field(TestWorld.RampNorth(0.2f)), TestWorld.Cmd(throttle: 1), 300);
        float c = MathF.Cos(MathF.Atan(0.2f));
        Assert.Equal(Spec.MaxForwardSpeed * c * c, t.Speed, 0.01f);
    }

    [Fact]
    public void SlopeSteeperThanMaxClimb_CannotBeClimbed()
    {
        var t = TestWorld.Tank(100, 150);
        TestWorld.Drive(ref t, TestWorld.Field(TestWorld.RampNorth(0.84f)), TestWorld.Cmd(throttle: 1), 120); // ~40°
        Assert.Equal(150f, t.Position.Y);
        Assert.Equal(0f, t.Speed);
    }

    [Fact]
    public void SteepSlope_CanBeDescended()
    {
        var t = TestWorld.Tank(100, 100, yaw: MathF.PI); // facing +Z, downhill
        TestWorld.Drive(ref t, TestWorld.Field(TestWorld.RampNorth(0.84f)), TestWorld.Cmd(throttle: 1), 60);
        Assert.True(t.Position.Y > 105, $"z was {t.Position.Y}");
    }
}
```

- [ ] **Step 3: 运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: 编译失败，`'InputCmd' could not be found`、`'TankMotion' does not exist`

- [ ] **Step 4: 实现**

`src/Tank.Sim/InputCmd.cs`：

```csharp
namespace Tank.Sim;

/// <summary>
/// One tick of driver input. <see cref="Throttle"/> and <see cref="Steer"/> are -1, 0 or 1 (Steer +1 = turn right).
/// Aim is a world-space direction from the gun trunnion (yaw as in Geometry.Angles, pitch positive up).
/// </summary>
public readonly record struct InputCmd(int Tick, sbyte Throttle, sbyte Steer, float AimYaw, float AimPitch, bool Fire);
```

`src/Tank.Sim/Vehicles/TankMotion.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;

namespace Tank.Sim.Vehicles;

/// <summary>Ground-hugging hull kinematics (design §4.1–4.2).</summary>
public static class TankMotion
{
    /// <summary>Above this speed the hull counts as moving and turns slower.</summary>
    private const float MovingThreshold = 0.5f;
    private const float MovingTurnFactor = 0.75f;
    private const int CollisionIterations = 3;

    /// <summary>If collisions leave less than this fraction of the intended step, the hull was blocked head-on and stalls.</summary>
    private const float StallRatio = 0.25f;

    public static Obb2 Footprint(TankSpec spec, Vector2 center, float yaw) =>
        new(center, new Vector2(spec.HullWidth / 2, spec.HullLength / 2), yaw);

    /// <summary>Applies throttle and steering, then moves the hull and pushes it out of props and <paramref name="obstacles"/>.</summary>
    public static void Drive(ref TankState t, in InputCmd input, Battlefield field, ReadOnlySpan<Obb2> obstacles)
    {
        TankSpec spec = t.Spec;
        const float dt = SimConstants.Dt;

        float turnRate = spec.HullTraverse * (MathF.Abs(t.Speed) > MovingThreshold ? MovingTurnFactor : 1f);
        t.Yaw = Angles.Wrap(t.Yaw - System.Math.Sign(input.Steer) * turnRate * dt);

        float target = TargetSpeed(t, input.Throttle, field.Terrain);
        bool accelerating = target * t.Speed >= 0 && MathF.Abs(target) > MathF.Abs(t.Speed);
        t.Speed = MoveTowards(t.Speed, target, (accelerating ? spec.Acceleration : spec.Braking) * dt);

        Vector2 start = t.Position;
        Vector2 intended = Angles.Forward(t.Yaw) * (t.Speed * dt);
        Vector2 moved = Resolve(Footprint(spec, start + intended, t.Yaw), field, obstacles);
        t.Position = field.ClampToPlayable(moved);

        // Pushed almost straight back (head-on into a rock, a tank or the map edge): stall.
        // A glancing contact keeps its speed so the hull slides along the obstacle.
        float intendedLength = intended.Length();
        if (intendedLength > 0 && Vector2.Distance(t.Position, start) < intendedLength * StallRatio) t.Speed = 0f;
    }

    /// <summary>Hull pitch along its facing: positive when the nose points uphill.</summary>
    public static float ForwardSlope(in TankState t, HeightField terrain)
    {
        Vector2 half = Angles.Forward(t.Yaw) * (t.Spec.HullLength * 0.5f);
        float front = terrain.HeightAt(t.Position.X + half.X, t.Position.Y + half.Y);
        float back = terrain.HeightAt(t.Position.X - half.X, t.Position.Y - half.Y);
        return MathF.Atan2(front - back, t.Spec.HullLength);
    }

    private static float TargetSpeed(in TankState t, sbyte throttle, HeightField terrain)
    {
        TankSpec spec = t.Spec;
        float target = throttle > 0 ? spec.MaxForwardSpeed : throttle < 0 ? -spec.MaxReverseSpeed : 0f;
        float uphill = ForwardSlope(t, terrain) * MathF.Sign(target);
        if (uphill <= 0) return target;
        if (uphill >= spec.MaxClimb) return 0f;
        float c = MathF.Cos(uphill);
        return target * c * c;
    }

    private static Vector2 Resolve(Obb2 box, Battlefield field, ReadOnlySpan<Obb2> obstacles)
    {
        IReadOnlyList<Prop> props = field.Props;
        for (int iteration = 0; iteration < CollisionIterations; iteration++)
        {
            bool pushed = false;
            for (int i = 0; i < props.Count; i++) pushed |= PushOut(ref box, props[i].Footprint);
            foreach (Obb2 other in obstacles) pushed |= PushOut(ref box, other);
            if (!pushed) break;
        }
        return box.Center;
    }

    private static bool PushOut(ref Obb2 box, in Obb2 obstacle)
    {
        float reach = box.HalfExtents.Length() + obstacle.HalfExtents.Length();
        if (Vector2.DistanceSquared(box.Center, obstacle.Center) > reach * reach) return false;
        if (!Obb2.TryGetPenetration(box, obstacle, out Vector2 push)) return false;
        box = box.WithCenter(box.Center + push);
        return true;
    }

    internal static float MoveTowards(float current, float target, float maxStep) =>
        MathF.Abs(target - current) <= maxStep ? target : current + MathF.CopySign(maxStep, target - current);
}
```

- [ ] **Step 5: 运行，确认通过**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: `Failed: 0, Passed: 52`

- [ ] **Step 6: 提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): hull driving with slope limits"
```

---

### Task 8：碰撞与边界

**Files:**
- Modify: `tests/Tank.Sim.Tests/TankMotionTests.cs`（追加测试；实现已在 Task 7 中）

这一步验证 Task 7 的碰撞代码。如果测试一次就通过，说明 Task 7 的实现已覆盖；如果失败，修 `TankMotion.Resolve` 或 `PushOut`。

- [ ] **Step 1: 追加测试**

在 `tests/Tank.Sim.Tests/TankMotionTests.cs` 顶部的 `using` 区加入：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
```

在类里追加：

```csharp
    [Fact]
    public void Rock_BlocksTheHull_AndStallsSpeed()
    {
        // 10m-wide, 2m-thick wall whose near face is at z = 81.
        var wall = new Prop(1, PropKind.Rock, new Obb2(new Vector2(100, 80), new Vector2(5, 1), 0), -1, 4);
        var t = TestWorld.Tank(100, 100);
        TestWorld.Drive(ref t, TestWorld.FlatField(wall), TestWorld.Cmd(throttle: 1), 300);
        float stopZ = 81 + Spec.HullLength / 2;
        Assert.InRange(t.Position.Y, stopZ - 0.01f, stopZ + 0.5f);
        Assert.True(t.Speed < 0.5f, $"speed was {t.Speed}");
    }

    [Fact]
    public void Rock_HitAtAnAngle_LetsTheHullSlideAlong()
    {
        // 160m-long wall so the tank is still alongside it after 10 s of sliding west.
        var wall = new Prop(1, PropKind.Rock, new Obb2(new Vector2(100, 80), new Vector2(80, 1), 0), -1, 4);
        var t = TestWorld.Tank(100, 100, yaw: Angles.Deg(30)); // heading north-west into the wall
        TestWorld.Drive(ref t, TestWorld.FlatField(wall), TestWorld.Cmd(throttle: 1), 300);
        Assert.True(t.Position.X < 90, $"x was {t.Position.X}");
        Assert.True(t.Position.Y > 81, $"z was {t.Position.Y}");
    }

    [Fact]
    public void OtherTank_BlocksLikeAnObstacle()
    {
        Obb2 parked = TankMotion.Footprint(Spec, new Vector2(100, 90), 0);
        var t = TestWorld.Tank(100, 100);
        TestWorld.Drive(ref t, TestWorld.FlatField(), TestWorld.Cmd(throttle: 1), 120, parked);
        Assert.True(t.Position.Y >= 90 + Spec.HullLength - 0.01f, $"z was {t.Position.Y}");
    }

    [Fact]
    public void Tank_CannotLeaveThePlayableArea()
    {
        var t = TestWorld.Tank(100, 10);
        TestWorld.Drive(ref t, TestWorld.FlatField(), TestWorld.Cmd(throttle: 1), 300);
        Assert.Equal(TestWorld.Border, t.Position.Y, 1e-3f);
    }
```

- [ ] **Step 2: 运行**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: `Failed: 0, Passed: 56`

- [ ] **Step 3: 提交**

```bash
git add tests/Tank.Sim.Tests/TankMotionTests.cs
git commit -m "test(sim): hull collisions and map bounds"
```

---

### Task 9：炮塔与炮管跟随瞄准

**Files:**
- Modify: `src/Tank.Sim/Vehicles/TankMotion.cs`（追加方法）
- Create: `tests/Tank.Sim.Tests/TankAimTests.cs`

- [ ] **Step 1: 写失败的测试**

`tests/Tank.Sim.Tests/TankAimTests.cs`：

```csharp
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class TankAimTests
{
    private static readonly TankSpec Spec = TankCatalog.Medium;

    private static void Aim(ref TankState t, InputCmd cmd, int ticks)
    {
        var terrain = TestWorld.Flat();
        for (int i = 0; i < ticks; i++) TankMotion.AimGun(ref t, cmd, terrain);
    }

    [Fact]
    public void Turret_TurnsTowardAim_AtTraverseSpeed()
    {
        var t = TestWorld.Tank(100, 100);
        var cmd = TestWorld.Cmd(aimYaw: MathF.PI / 2);
        Aim(ref t, cmd, 1);
        Assert.Equal(Spec.TurretTraverse * SimConstants.Dt, t.TurretYaw, 1e-4f);
        Aim(ref t, cmd, 300);
        Assert.Equal(MathF.PI / 2, t.TurretYaw, 1e-4f);
    }

    [Fact]
    public void Turret_HoldsWorldAim_WhenHullIsTurned()
    {
        var t = TestWorld.Tank(100, 100, yaw: 0.5f);
        Aim(ref t, TestWorld.Cmd(aimYaw: 0f), 300);
        Assert.Equal(-0.5f, t.TurretYaw, 1e-4f);
    }

    [Fact]
    public void Gun_IsClampedToElevationAndDepression()
    {
        var t = TestWorld.Tank(100, 100);
        Aim(ref t, TestWorld.Cmd(aimPitch: 1.2f), 300);
        Assert.Equal(Spec.GunElevation, t.GunPitch, 1e-4f);
        Aim(ref t, TestWorld.Cmd(aimPitch: -1.2f), 300);
        Assert.Equal(-Spec.GunDepression, t.GunPitch, 1e-4f);
    }
}
```

- [ ] **Step 2: 运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: 编译失败，`'TankMotion' does not contain a definition for 'AimGun'`

- [ ] **Step 3: 实现**

在 `src/Tank.Sim/Vehicles/TankMotion.cs` 中，紧接 `Drive` 方法之后加入：

```csharp
    /// <summary>Turns turret and gun toward the input's world aim direction at their rated speeds, within gun limits.</summary>
    public static void AimGun(ref TankState t, in InputCmd input, HeightField terrain)
    {
        TankSpec spec = t.Spec;
        TankPose pose = TankPose.Compute(t, terrain);
        var (wantYaw, wantPitch) = pose.ToHullAngles(Angles.Direction(input.AimYaw, input.AimPitch));
        t.TurretYaw = Angles.MoveTowards(t.TurretYaw, wantYaw, spec.TurretTraverse * SimConstants.Dt);
        float pitch = System.Math.Clamp(wantPitch, -spec.GunDepression, spec.GunElevation);
        t.GunPitch = MoveTowards(t.GunPitch, pitch, spec.GunPitchSpeed * SimConstants.Dt);
    }
```

- [ ] **Step 4: 运行，确认通过**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: `Failed: 0, Passed: 59`

- [ ] **Step 5: 提交**

```bash
git add src/Tank.Sim/Vehicles/TankMotion.cs tests/Tank.Sim.Tests/TankAimTests.cs
git commit -m "feat(sim): turret and gun follow world aim"
```

---

### Task 10：炮弹与弹道

**Files:**
- Create: `src/Tank.Sim/Combat/Shell.cs`
- Create: `src/Tank.Sim/Combat/Ballistics.cs`
- Create: `tests/Tank.Sim.Tests/BallisticsTests.cs`

- [ ] **Step 1: 写失败的测试**

`tests/Tank.Sim.Tests/BallisticsTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Sim.Tests;

public sealed class BallisticsTests
{
    private static readonly Vector3 Origin = new(20, 5, 100);
    private static readonly Vector3 Velocity = new(100, 0, 0);

    private static RayHit Fly(Battlefield field)
    {
        var shell = new Shell { Position = Origin, Velocity = Velocity };
        while (shell.Age < Ballistics.MaxLifetime)
            if (Ballistics.Step(ref shell, field, out var hit)) return hit;
        throw new Xunit.Sdk.XunitException("Shell never landed.");
    }

    [Fact]
    public void HorizontalShot_LandsWhereKinematicsPredict()
    {
        float expectedX = Origin.X + Velocity.X * MathF.Sqrt(2 * Origin.Y / SimConstants.Gravity);
        RayHit hit = Fly(TestWorld.FlatField());
        Assert.Equal(expectedX, hit.Point.X, 0.3f);
        Assert.Equal(0f, hit.Point.Y, 0.01f);
    }

    [Fact]
    public void PredictImpact_MatchesSteppedFlightExactly()
    {
        var field = TestWorld.FlatField();
        Assert.True(Ballistics.PredictImpact(Origin, Velocity, field, out Vector3 predicted));
        Assert.Equal(Fly(field).Point, predicted);
    }

    [Fact]
    public void Shell_HitsRockBeforeGround()
    {
        var rock = new Prop(7, PropKind.Rock, new Obb2(new Vector2(60, 100), new Vector2(1, 5), 0), -1, 10);
        RayHit hit = Fly(TestWorld.FlatField(rock));
        Assert.Equal(7, hit.PropId);
        Assert.Equal(59f, hit.Point.X, 0.05f);
    }

    [Fact]
    public void PredictImpact_ShotStraightUp_ExpiresWithoutHit() =>
        Assert.False(Ballistics.PredictImpact(new Vector3(100, 1, 100), new Vector3(0, 30, 0), TestWorld.FlatField(), out _));
}
```

- [ ] **Step 2: 运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: 编译失败，`The type or namespace name 'Combat' does not exist`

- [ ] **Step 3: 实现**

`src/Tank.Sim/Combat/Shell.cs`：

```csharp
using System.Numerics;

namespace Tank.Sim.Combat;

public struct Shell
{
    public int Id;
    public int OwnerTankId;
    public Vector3 Position;
    public Vector3 Velocity;

    /// <summary>Seconds since it left the muzzle.</summary>
    public float Age;
}
```

`src/Tank.Sim/Combat/Ballistics.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Maps;

namespace Tank.Sim.Combat;

/// <summary>
/// Exact constant-gravity kinematics per tick, then a segment test between the old and new positions.
/// The client's aim prediction runs this same code, so the reticle shows where the shell really lands.
/// </summary>
public static class Ballistics
{
    public const float MaxLifetime = 4f;

    private static readonly Vector3 GravityStep = new(0, -SimConstants.Gravity * SimConstants.Dt, 0);
    private static readonly Vector3 GravityDrop = new(0, -0.5f * SimConstants.Gravity * SimConstants.Dt * SimConstants.Dt, 0);

    /// <summary>Advances one tick. True if the path this tick hit the ground or a prop.</summary>
    public static bool Step(ref Shell shell, Battlefield field, out RayHit hit)
    {
        Vector3 from = shell.Position;
        Vector3 to = from + shell.Velocity * SimConstants.Dt + GravityDrop;
        shell.Velocity += GravityStep;
        shell.Position = to;
        shell.Age += SimConstants.Dt;
        return field.Raycast(from, to, out hit);
    }

    /// <summary>Where a shell fired now would land. False if it is still flying after <see cref="MaxLifetime"/>.</summary>
    public static bool PredictImpact(Vector3 origin, Vector3 velocity, Battlefield field, out Vector3 point)
    {
        var shell = new Shell { Position = origin, Velocity = velocity };
        while (shell.Age < MaxLifetime)
        {
            if (Step(ref shell, field, out RayHit hit))
            {
                point = hit.Point;
                return true;
            }
        }
        point = shell.Position;
        return false;
    }
}
```

- [ ] **Step 4: 运行，确认通过**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: `Failed: 0, Passed: 63`

- [ ] **Step 5: 提交**

```bash
git add src/Tank.Sim/Combat tests/Tank.Sim.Tests/BallisticsTests.cs
git commit -m "feat(sim): shell ballistics and impact prediction"
```

---

### Task 11：Simulation（开火、装填、炮弹生命周期）

**Files:**
- Create: `src/Tank.Sim/GameEvent.cs`
- Create: `src/Tank.Sim/Simulation.cs`
- Create: `tests/Tank.Sim.Tests/SimulationTests.cs`

- [ ] **Step 1: 写失败的测试**

`tests/Tank.Sim.Tests/SimulationTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class SimulationTests
{
    private static readonly TankSpec Spec = TankCatalog.Medium;

    private static Simulation NewSim(out int tankId)
    {
        var sim = new Simulation(TestWorld.FlatField());
        tankId = sim.AddTank(Spec, new Vector2(100, 100), 0f);
        return sim;
    }

    private static List<GameEvent> Run(Simulation sim, InputCmd cmd, int ticks)
    {
        var events = new List<GameEvent>();
        var inputs = new[] { cmd };
        for (int i = 0; i < ticks; i++)
        {
            sim.Step(inputs);
            events.AddRange(sim.Events);
        }
        return events;
    }

    [Fact]
    public void Step_AdvancesTick()
    {
        var sim = NewSim(out _);
        Run(sim, TestWorld.Cmd(), 3);
        Assert.Equal(3, sim.Tick);
    }

    [Fact]
    public void Step_RejectsWrongInputCount()
    {
        var sim = NewSim(out _);
        Assert.Throws<ArgumentException>(() => sim.Step(Array.Empty<InputCmd>()));
    }

    [Fact]
    public void AddTank_ClampsIntoPlayableArea()
    {
        var sim = new Simulation(TestWorld.FlatField());
        sim.AddTank(Spec, new Vector2(-50, 500), 0f);
        Assert.Equal(new Vector2(TestWorld.Border, TestWorld.Size - TestWorld.Border), sim.Tanks[0].Position);
    }

    [Fact]
    public void Fire_SpawnsShellAtMuzzle_AndEmitsEvent()
    {
        var sim = NewSim(out int tankId);
        var events = Run(sim, TestWorld.Cmd(fire: true), 1);

        Assert.Equal(1, sim.ShellCount);
        GameEvent fired = Assert.Single(events, e => e.Type == GameEventType.ShellFired);
        Assert.Equal(tankId, fired.TankId);
        var expectedMuzzle = new Vector3(100, Spec.TrunnionHeight, 100 - Spec.TurretOffset - Spec.GunLength);
        Assert.True(Vector3.Distance(expectedMuzzle, fired.Position) < 1e-3f, $"muzzle at {fired.Position}");
        Assert.Equal(Spec.ShellSpeed, sim.Shells[0].Velocity.Length(), 0.5f);
        Assert.Equal(Spec.ReloadTime, sim.Tanks[0].Reload, 1e-4f);
    }

    [Fact]
    public void Reload_GatesTheNextShot()
    {
        var sim = NewSim(out _);
        Run(sim, TestWorld.Cmd(fire: true), (int)(Spec.ReloadTime * SimConstants.TickRate) + 15);
        Assert.Equal(2, sim.Tanks[0].ShotCount);
    }

    [Fact]
    public void ShellAimedAtTheGround_ExplodesAheadOfTheTank()
    {
        var sim = NewSim(out _);
        Run(sim, TestWorld.Cmd(aimPitch: -0.3f), 30); // let the gun settle at full depression
        var events = Run(sim, TestWorld.Cmd(aimPitch: -0.3f, fire: true), 60);

        GameEvent boom = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.Equal(0f, boom.Position.Y, 0.05f);
        Assert.InRange(boom.Position.Z, 60f, 95f);
        Assert.Equal(0, sim.ShellCount);
    }
}
```

- [ ] **Step 2: 运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: 编译失败，`'Simulation' could not be found`

- [ ] **Step 3: 实现**

`src/Tank.Sim/GameEvent.cs`：

```csharp
using System.Numerics;

namespace Tank.Sim;

public enum GameEventType : byte
{
    ShellFired,
    ShellExploded,
}

/// <summary>Something presentation should react to. <see cref="Velocity"/> is zero when not meaningful.</summary>
public readonly record struct GameEvent(GameEventType Type, int Tick, int TankId, int ShellId, Vector3 Position, Vector3 Velocity);
```

`src/Tank.Sim/Simulation.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim;

/// <summary>
/// Advances the battle by one fixed 30Hz tick. Order inside a tick: each tank drives, aims and fires
/// (in slot order), then every shell flies and resolves its hits.
/// </summary>
public sealed class Simulation
{
    public const int MaxTanks = 32;
    public const int MaxShells = 256;

    private readonly TankState[] _tanks = new TankState[MaxTanks];
    private readonly Shell[] _shells = new Shell[MaxShells];
    private readonly Obb2[] _footprints = new Obb2[MaxTanks];
    private readonly List<GameEvent> _events = new(16);
    private int _nextId = 1;

    public Simulation(Battlefield field) => Field = field;

    public Battlefield Field { get; }
    public int Tick { get; private set; }
    public int TankCount { get; private set; }
    public int ShellCount { get; private set; }
    public ReadOnlySpan<TankState> Tanks => _tanks.AsSpan(0, TankCount);
    public ReadOnlySpan<Shell> Shells => _shells.AsSpan(0, ShellCount);

    /// <summary>Events produced by the most recent <see cref="Step"/>.</summary>
    public IReadOnlyList<GameEvent> Events => _events;

    /// <returns>The new tank's id. It occupies the next slot in <see cref="Tanks"/>.</returns>
    public int AddTank(TankSpec spec, Vector2 position, float yaw)
    {
        if (TankCount == MaxTanks) throw new InvalidOperationException($"At most {MaxTanks} tanks.");
        int id = _nextId++;
        _tanks[TankCount++] = new TankState { Id = id, Spec = spec, Position = Field.ClampToPlayable(position), Yaw = Angles.Wrap(yaw) };
        return id;
    }

    /// <param name="inputs">One command per tank, in <see cref="Tanks"/> order.</param>
    public void Step(ReadOnlySpan<InputCmd> inputs)
    {
        if (inputs.Length != TankCount)
            throw new ArgumentException($"Expected {TankCount} inputs, got {inputs.Length}.", nameof(inputs));
        _events.Clear();
        Tick++;
        for (int i = 0; i < TankCount; i++)
        {
            ref TankState tank = ref _tanks[i];
            TankMotion.Drive(ref tank, inputs[i], Field, FootprintsExcept(i));
            TankMotion.AimGun(ref tank, inputs[i], Field.Terrain);
            TryFire(ref tank, inputs[i]);
        }
        StepShells();
    }

    private ReadOnlySpan<Obb2> FootprintsExcept(int index)
    {
        int n = 0;
        for (int j = 0; j < TankCount; j++)
        {
            if (j == index) continue;
            ref readonly TankState other = ref _tanks[j];
            _footprints[n++] = TankMotion.Footprint(other.Spec, other.Position, other.Yaw);
        }
        return _footprints.AsSpan(0, n);
    }

    private void TryFire(ref TankState tank, in InputCmd input)
    {
        tank.Reload = MathF.Max(0, tank.Reload - SimConstants.Dt);
        if (!input.Fire || tank.Reload > 0 || ShellCount == MaxShells) return;

        TankPose pose = TankPose.Compute(tank, Field.Terrain);
        var shell = new Shell
        {
            Id = _nextId++,
            OwnerTankId = tank.Id,
            Position = pose.Muzzle(tank.Spec, tank.TurretYaw, tank.GunPitch),
            Velocity = pose.GunDirection(tank.TurretYaw, tank.GunPitch) * tank.Spec.ShellSpeed,
        };
        _shells[ShellCount++] = shell;
        tank.Reload = tank.Spec.ReloadTime;
        tank.ShotCount++;
        _events.Add(new GameEvent(GameEventType.ShellFired, Tick, tank.Id, shell.Id, shell.Position, shell.Velocity));
    }

    private void StepShells()
    {
        for (int i = ShellCount - 1; i >= 0; i--)
        {
            ref Shell shell = ref _shells[i];
            bool hit = Ballistics.Step(ref shell, Field, out RayHit rayHit);
            if (!hit && shell.Age < Ballistics.MaxLifetime) continue;
            if (hit)
                _events.Add(new GameEvent(GameEventType.ShellExploded, Tick, shell.OwnerTankId, shell.Id, rayHit.Point, Vector3.Zero));
            _shells[i] = _shells[--ShellCount];
        }
    }
}
```

- [ ] **Step 4: 运行，确认通过**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: `Failed: 0, Passed: 69`

- [ ] **Step 5: 提交**

```bash
git add src/Tank.Sim/GameEvent.cs src/Tank.Sim/Simulation.cs tests/Tank.Sim.Tests/SimulationTests.cs
git commit -m "feat(sim): simulation loop with firing, reload and shells"
```

---

### Task 12：试驾场地图

**Files:**
- Create: `src/Tank.Sim/Maps/SandboxMap.cs`
- Create: `tests/Tank.Sim.Tests/SandboxMapTests.cs`

- [ ] **Step 1: 写失败的测试**

`tests/Tank.Sim.Tests/SandboxMapTests.cs`：

```csharp
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class SandboxMapTests
{
    [Fact]
    public void Create_IsDeterministic()
    {
        Battlefield a = SandboxMap.Create(), b = SandboxMap.Create();
        Assert.Equal(a.Props.Count, b.Props.Count);
        Assert.Equal(a.Props[0], b.Props[0]);
        Assert.Equal(a.Terrain.HeightAt(123, 45), b.Terrain.HeightAt(123, 45));
    }

    [Fact]
    public void Spawn_IsPlayable_AndClearOfRocks()
    {
        Battlefield field = SandboxMap.Create();
        Assert.Equal(SandboxMap.Spawn, field.ClampToPlayable(SandboxMap.Spawn));
        Obb2 hull = TankMotion.Footprint(TankCatalog.Medium, SandboxMap.Spawn, 0);
        Assert.All(field.Props, p => Assert.False(Obb2.TryGetPenetration(hull, p.Footprint, out _)));
    }

    [Fact]
    public void Terrain_HasAHillWorthClimbing()
    {
        Battlefield field = SandboxMap.Create();
        float rise = field.Terrain.HeightAt(200, 150) - field.Terrain.HeightAt(SandboxMap.Spawn.X, SandboxMap.Spawn.Y);
        Assert.True(rise > 15, $"hill only {rise}m above spawn");
    }

    [Fact]
    public void Rocks_SitOnTheGround()
    {
        Battlefield field = SandboxMap.Create();
        Assert.All(field.Props, p =>
            Assert.True(p.Top > field.Terrain.HeightAt(p.Footprint.Center.X, p.Footprint.Center.Y) + 1f, $"rock {p.Id} is buried"));
    }
}
```

- [ ] **Step 2: 运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: 编译失败，`'SandboxMap' does not exist`

- [ ] **Step 3: 实现**

`src/Tank.Sim/Maps/SandboxMap.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Terrain;

namespace Tank.Sim.Maps;

/// <summary>400m × 400m test ground: rolling noise, four hills, scattered rocks. The spawn faces the big hill (north).</summary>
public static class SandboxMap
{
    public const int Seed = 20261013;
    public const int Samples = 201;
    public const float CellSize = 2f;
    public const float Border = 10f;
    private const int RockAttempts = 48;
    private const float SpawnClearance = 30f;

    public static readonly Vector2 Spawn = new(200, 330);

    private static readonly TerrainGenerator.Hill[] Hills =
    {
        new(200, 150, 70, 22),
        new(90, 250, 45, 12),
        new(310, 230, 55, 16),
        new(140, 90, 35, 9),
    };

    public static Battlefield Create()
    {
        HeightField terrain = TerrainGenerator.Generate(Seed, Samples, CellSize, roughness: 4f, Hills);
        return new Battlefield(terrain, ScatterRocks(terrain), Border);
    }

    private static List<Prop> ScatterRocks(HeightField terrain)
    {
        var rng = new XorShift(Seed);
        var rocks = new List<Prop>();
        for (int k = 0; k < RockAttempts; k++)
        {
            var center = new Vector2(40 + rng.NextFloat() * 320, 40 + rng.NextFloat() * 320);
            var half = new Vector2(1 + rng.NextFloat() * 2, 1 + rng.NextFloat() * 2.5f);
            float height = 1.5f + rng.NextFloat() * 3f;
            float yaw = rng.NextFloat() * Angles.TwoPi;
            if (Vector2.Distance(center, Spawn) < SpawnClearance) continue;
            float ground = terrain.HeightAt(center.X, center.Y);
            // Sink the base 1m so the rock never floats on a slope.
            rocks.Add(new Prop(rocks.Count + 1, PropKind.Rock, new Obb2(center, half, yaw), ground - 1f, height + 1f));
        }
        return rocks;
    }
}
```

- [ ] **Step 4: 运行，确认通过**

Run: `dotnet test tests/Tank.Sim.Tests`
Expected: `Failed: 0, Passed: 73`

如果 `Terrain_HasAHillWorthClimbing` 失败，说明出生点附近的噪声偏高。把 `Hills` 第一项的高度从 22 调到 26 后重跑，不要改测试。

- [ ] **Step 5: 提交**

```bash
git add src/Tank.Sim/Maps/SandboxMap.cs tests/Tank.Sim.Tests/SandboxMapTests.cs
git commit -m "feat(sim): sandbox test map"
```

---

### Task 13：接入 CI，检查覆盖率

**Files:**
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: 加测试步骤**

在 `.github/workflows/ci.yml` 中，把这一行：

```yaml
          dotnet test tests/Tank.Core.Tests --collect:"XPlat Code Coverage" --results-directory coverage/core
```

替换为：

```yaml
          dotnet test tests/Tank.Core.Tests --collect:"XPlat Code Coverage" --results-directory coverage/core
          dotnet test tests/Tank.Sim.Tests --collect:"XPlat Code Coverage" --results-directory coverage/sim
```

- [ ] **Step 2: 本地跑覆盖率**

Run:

```bash
dotnet test tests/Tank.Sim.Tests --collect:"XPlat Code Coverage" --results-directory coverage/sim
grep -o 'line-rate="[0-9.]*"' coverage/sim/*/coverage.cobertura.xml | head -1
```

Expected: `line-rate` ≥ `0.8`。如果低于 0.8，看 XML 中哪个类偏低，为它补测试再提交。

- [ ] **Step 3: 提交**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: run Tank.Sim tests"
```

---

### Task 14：客户端引用 Tank.Sim，渲染地形与岩石

**Files:**
- Modify: `src/Tank.Client/Tank.Client.csproj`
- Create: `src/Tank.Client/scripts/World/SimConvert.cs`
- Create: `src/Tank.Client/scripts/World/Scenery.cs`
- Create: `src/Tank.Client/scripts/World/TerrainMesh.cs`
- Create: `src/Tank.Client/scripts/World/PropMeshes.cs`

Godot 视图代码不写单测，用 Task 18 和 Task 19 的截图与试玩验收。每个客户端任务都必须能编译通过。

- [ ] **Step 1: 加引用**

在 `src/Tank.Client/Tank.Client.csproj` 的 `<ItemGroup>` 中，`Tank.Core` 那一行下面加：

```xml
    <ProjectReference Include="..\Tank.Sim\Tank.Sim.csproj" />
```

- [ ] **Step 2: 类型转换**

`src/Tank.Client/scripts/World/SimConvert.cs`：

```csharp
using Godot;
using SN = System.Numerics;

namespace Tank.Client.World;

/// <summary>Tank.Sim uses System.Numerics with Godot's axes (Y up, -Z forward), so conversion is a plain copy.</summary>
public static class SimConvert
{
    public static Vector3 ToGodot(this SN.Vector3 v) => new(v.X, v.Y, v.Z);

    public static SN.Vector3 ToSim(this Vector3 v) => new(v.X, v.Y, v.Z);

    public static Quaternion ToGodot(this SN.Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    /// <summary>Ground point (x, z) lifted to height <paramref name="y"/>.</summary>
    public static Vector3 Ground(this SN.Vector2 p, float y) => new(p.X, y, p.Y);
}
```

- [ ] **Step 3: 天空和光照**

`src/Tank.Client/scripts/World/Scenery.cs`：

```csharp
using Godot;

namespace Tank.Client.World;

/// <summary>Sky, haze and sun for outdoor maps.</summary>
public static class Scenery
{
    public static WorldEnvironment CreateEnvironment()
    {
        var sky = new Sky
        {
            SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.30f, 0.50f, 0.78f),
                SkyHorizonColor = new Color(0.74f, 0.80f, 0.85f),
                GroundHorizonColor = new Color(0.60f, 0.62f, 0.58f),
                GroundBottomColor = new Color(0.25f, 0.27f, 0.22f),
            },
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.8f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogLightColor = new Color(0.72f, 0.78f, 0.84f),
            FogDensity = 0.0018f,
        };
        return new WorldEnvironment { Environment = environment };
    }

    public static DirectionalLight3D CreateSun() => new()
    {
        RotationDegrees = new Vector3(-48, -35, 0),
        LightEnergy = 1.25f,
        ShadowEnabled = true,
        DirectionalShadowMaxDistance = 220f,
    };
}
```

- [ ] **Step 4: 地形网格**

`src/Tank.Client/scripts/World/TerrainMesh.cs`：

```csharp
using Godot;
using Tank.Sim.Terrain;

namespace Tank.Client.World;

/// <summary>
/// Flat-shaded low-poly ground built from the sim's height field. Split into chunks so the renderer can cull
/// what is off screen. Colour comes from slope (grass → dirt → rock) with a little per-cell variation.
/// </summary>
public partial class TerrainMesh : Node3D
{
    private const int ChunkCells = 32;
    private const float RockBelowUp = 0.82f;  // steeper than ~35°
    private const float DirtBelowUp = 0.94f;  // steeper than ~20°
    private const float DryAtHeight = 30f;

    private static readonly Color Grass = new(0.36f, 0.47f, 0.24f);
    private static readonly Color DryGrass = new(0.52f, 0.52f, 0.30f);
    private static readonly Color Dirt = new(0.47f, 0.38f, 0.26f);
    private static readonly Color Rock = new(0.46f, 0.45f, 0.43f);

    public void Build(HeightField field)
    {
        var material = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 1f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        int cellsX = field.SamplesX - 1, cellsZ = field.SamplesZ - 1;
        for (int z0 = 0; z0 < cellsZ; z0 += ChunkCells)
        for (int x0 = 0; x0 < cellsX; x0 += ChunkCells)
        {
            var chunk = BuildChunk(field, x0, z0, Math.Min(ChunkCells, cellsX - x0), Math.Min(ChunkCells, cellsZ - z0));
            AddChild(new MeshInstance3D { Mesh = chunk, MaterialOverride = material });
        }
    }

    private static ArrayMesh BuildChunk(HeightField field, int x0, int z0, int width, int depth)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int j = z0; j < z0 + depth; j++)
        for (int i = x0; i < x0 + width; i++)
        {
            Vector3 a = Point(field, i, j), b = Point(field, i + 1, j), c = Point(field, i, j + 1), d = Point(field, i + 1, j + 1);
            // Alternate the diagonal so long slopes don't streak in one direction.
            if (((i + j) & 1) == 0)
            {
                AddTriangle(st, a, b, d, i, j);
                AddTriangle(st, a, d, c, i, j);
            }
            else
            {
                AddTriangle(st, a, b, c, i, j);
                AddTriangle(st, b, d, c, i, j);
            }
        }
        return st.Commit();
    }

    private static Vector3 Point(HeightField field, int i, int j) => new(i * field.CellSize, field.Sample(i, j), j * field.CellSize);

    private static void AddTriangle(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, int i, int j)
    {
        Vector3 normal = (b - a).Cross(c - a).Normalized();
        if (normal.Y < 0) normal = -normal;
        st.SetNormal(normal);
        st.SetColor(Shade(normal.Y, (a.Y + b.Y + c.Y) / 3f, i, j));
        st.AddVertex(a);
        st.AddVertex(b);
        st.AddVertex(c);
    }

    private static Color Shade(float up, float height, int i, int j)
    {
        if (up < RockBelowUp) return Rock;
        if (up < DirtBelowUp) return Dirt;
        float dryness = Hash(i, j) * 0.4f + Mathf.Clamp(height / DryAtHeight, 0f, 0.6f);
        return Grass.Lerp(DryGrass, dryness);
    }

    private static float Hash(int i, int j)
    {
        unchecked
        {
            uint h = (uint)i * 73856093u ^ (uint)j * 19349663u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
        }
    }
}
```

- [ ] **Step 5: 岩石网格**

`src/Tank.Client/scripts/World/PropMeshes.cs`：

```csharp
using Godot;
using Tank.Sim.Maps;

namespace Tank.Client.World;

/// <summary>One box per rock, matching the sim footprint and height exactly so what you see is what you hit.</summary>
public partial class PropMeshes : Node3D
{
    private static readonly Color RockColor = new(0.50f, 0.49f, 0.46f);

    public void Build(IReadOnlyList<Prop> props)
    {
        var material = new StandardMaterial3D { AlbedoColor = RockColor, Roughness = 0.95f };
        foreach (Prop prop in props)
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(prop.Footprint.HalfExtents.X * 2, prop.Height, prop.Footprint.HalfExtents.Y * 2) },
                MaterialOverride = material,
                Position = prop.Footprint.Center.Ground(prop.Bottom + prop.Height / 2),
                Rotation = new Vector3(0, prop.Footprint.Yaw, 0),
            });
        }
    }
}
```

- [ ] **Step 6: 编译**

Run: `dotnet build src/Tank.Client/Tank.Client.csproj`
Expected: `Build succeeded`

- [ ] **Step 7: 提交**

```bash
git add src/Tank.Client/Tank.Client.csproj src/Tank.Client/scripts/World
git commit -m "feat(client): terrain and rock meshes from Tank.Sim"
```

---

### Task 15：程序化坦克模型

**Files:**
- Create: `src/Tank.Client/scripts/Vehicles/TankModel.cs`

- [ ] **Step 1: 实现**

`src/Tank.Client/scripts/Vehicles/TankModel.cs`：

```csharp
using Godot;
using Tank.Client.World;
using Tank.Sim.Vehicles;

namespace Tank.Client.Vehicles;

/// <summary>
/// Low-poly tank built from boxes. Node layout mirrors the sim: hull origin on the ground, "Turret" at the ring
/// (yaw), "Gun" at the trunnion (pitch), barrel pointing -Z. A glTF model can replace it later if node names match.
/// </summary>
public partial class TankModel : Node3D
{
    private const float HullBottom = 0.4f;
    private const float HullTop = 1.3f;

    private static readonly Color HullColor = new(0.34f, 0.39f, 0.23f);
    private static readonly Color TurretColor = new(0.38f, 0.43f, 0.26f);
    private static readonly Color TrackColor = new(0.13f, 0.13f, 0.12f);
    private static readonly Color GunColor = new(0.27f, 0.30f, 0.19f);

    private readonly Node3D _turret = new() { Name = "Turret" };
    private readonly Node3D _gun = new() { Name = "Gun" };

    public void Build(TankSpec spec)
    {
        float w = spec.HullWidth, l = spec.HullLength;
        AddBox(this, new Vector3(w * 0.78f, HullTop - HullBottom, l * 0.96f), new Vector3(0, (HullTop + HullBottom) / 2, 0), HullColor);
        float trackX = w / 2 - w * 0.11f;
        foreach (float side in new[] { -1f, 1f })
            AddBox(this, new Vector3(w * 0.22f, 0.9f, l), new Vector3(side * trackX, 0.45f, 0), TrackColor);

        _turret.Position = new Vector3(0, 0, -spec.TurretOffset);
        AddChild(_turret);
        float turretTop = spec.TrunnionHeight + 0.35f;
        AddBox(_turret, new Vector3(w * 0.62f, turretTop - HullTop, l * 0.42f), new Vector3(0, (turretTop + HullTop) / 2, 0.4f), TurretColor);

        _gun.Position = new Vector3(0, spec.TrunnionHeight, 0);
        _turret.AddChild(_gun);
        AddBox(_gun, new Vector3(0.7f, 0.5f, 0.5f), new Vector3(0, 0, -0.1f), TurretColor); // mantlet
        _gun.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.13f, Height = spec.GunLength, RadialSegments = 8 },
            MaterialOverride = Flat(GunColor),
            Position = new Vector3(0, 0, -spec.GunLength / 2),
            RotationDegrees = new Vector3(-90, 0, 0),
        });
    }

    public void Apply(TankPose pose, float turretYaw, float gunPitch)
    {
        GlobalTransform = new Transform3D(new Basis(pose.Rotation.ToGodot()), pose.Position.ToGodot());
        _turret.Rotation = new Vector3(0, turretYaw, 0);
        _gun.Rotation = new Vector3(gunPitch, 0, 0);
    }

    private static void AddBox(Node3D parent, Vector3 size, Vector3 position, Color color) =>
        parent.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Flat(color), Position = position });

    private static StandardMaterial3D Flat(Color color) => new() { AlbedoColor = color, Roughness = 0.85f };
}
```

- [ ] **Step 2: 编译**

Run: `dotnet build src/Tank.Client/Tank.Client.csproj`
Expected: `Build succeeded`

- [ ] **Step 3: 提交**

```bash
git add src/Tank.Client/scripts/Vehicles
git commit -m "feat(client): procedural low-poly tank model"
```

---

### Task 16：第三人称镜头与狙击镜

**Files:**
- Create: `src/Tank.Client/scripts/Cameras/OrbitCamera.cs`

- [ ] **Step 1: 实现**

`src/Tank.Client/scripts/Cameras/OrbitCamera.cs`：

```csharp
using Godot;
using Tank.Sim.Terrain;

namespace Tank.Client.Cameras;

/// <summary>
/// Third-person camera orbiting the tank, driven by the mouse; sniper mode puts the eye above the gun with 2×/4×/8×
/// zoom. The screen centre is always where the player wants to aim. Pitch is positive looking down.
/// </summary>
public partial class OrbitCamera : Node3D
{
    private const float MinDistance = 8f;
    private const float MaxDistance = 30f;
    private const float DistanceStep = 2f;
    private const float FocusHeight = 1.6f;
    private const float GroundClearance = 1.2f;
    private const float MinPitch = -0.6f;
    private const float MaxPitch = 1.1f;
    private const float MouseSensitivity = 0.0025f;
    private const float BaseFov = 70f;
    private static readonly float[] Zooms = { 2f, 4f, 8f };

    private readonly Camera3D _camera = new() { Current = true, Fov = BaseFov, Near = 0.1f, Far = 2000f };
    private int _zoomIndex;

    public Camera3D Camera => _camera;
    public float Yaw { get; private set; }
    public float Pitch { get; private set; } = 0.25f;
    public float Distance { get; private set; } = 14f;
    public bool Sniper { get; private set; }
    public float Zoom => Zooms[_zoomIndex];

    public override void _Ready() => AddChild(_camera);

    public void Look(Vector2 mouseDelta)
    {
        float scale = MouseSensitivity / (Sniper ? Zoom : 1f);
        Yaw = Mathf.Wrap(Yaw - mouseDelta.X * scale, -Mathf.Pi, Mathf.Pi);
        Pitch = Mathf.Clamp(Pitch + mouseDelta.Y * scale, MinPitch, MaxPitch);
    }

    public void SetAngles(float yaw, float pitch)
    {
        Yaw = yaw;
        Pitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);
    }

    /// <summary>Positive = wheel up: zoom in (closer orbit or higher sniper magnification).</summary>
    public void Scroll(int steps)
    {
        if (Sniper) _zoomIndex = Math.Clamp(_zoomIndex + steps, 0, Zooms.Length - 1);
        else Distance = Mathf.Clamp(Distance - steps * DistanceStep, MinDistance, MaxDistance);
    }

    public void ToggleSniper() => Sniper = !Sniper;

    /// <param name="pivot">Hull origin the orbit circles.</param>
    /// <param name="sniperEye">Eye position in sniper mode, just above the gun.</param>
    public void Follow(Vector3 pivot, Vector3 sniperEye, HeightField terrain)
    {
        _camera.Rotation = new Vector3(-Pitch, Yaw, 0);
        if (Sniper)
        {
            _camera.Position = sniperEye;
            _camera.Fov = BaseFov / Zoom;
            return;
        }
        _camera.Fov = BaseFov;
        Vector3 look = -_camera.Basis.Z;
        Vector3 eye = pivot + Vector3.Up * FocusHeight - look * Distance;
        float floor = terrain.HeightAt(eye.X, eye.Z) + GroundClearance;
        if (eye.Y < floor) eye.Y = floor;
        _camera.Position = eye;
    }
}
```

- [ ] **Step 2: 编译**

Run: `dotnet build src/Tank.Client/Tank.Client.csproj`
Expected: `Build succeeded`

- [ ] **Step 3: 提交**

```bash
git add src/Tank.Client/scripts/Cameras
git commit -m "feat(client): third-person orbit camera with sniper zoom"
```

---

### Task 17：炮弹曳光、特效与准星

**Files:**
- Create: `src/Tank.Client/scripts/Combat/ShellViews.cs`
- Create: `src/Tank.Client/scripts/Combat/BattleFx.cs`
- Create: `src/Tank.Client/scripts/Overlay/Reticle.cs`

- [ ] **Step 1: 炮弹曳光**

`src/Tank.Client/scripts/Combat/ShellViews.cs`：

```csharp
using Godot;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Combat;

namespace Tank.Client.Combat;

/// <summary>Glowing tracers for live shells, pooled. Drawn one tick behind the sim to match interpolated tanks.</summary>
public partial class ShellViews : Node3D
{
    private readonly List<MeshInstance3D> _pool = new();
    private StandardMaterial3D _material = null!;

    public override void _Ready() => _material = new StandardMaterial3D
    {
        AlbedoColor = new Color(1f, 0.85f, 0.45f),
        EmissionEnabled = true,
        Emission = new Color(1f, 0.7f, 0.3f),
        EmissionEnergyMultiplier = 4f,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    /// <param name="alpha">Fraction of the way from the previous tick to the latest one.</param>
    public void Sync(ReadOnlySpan<Shell> shells, float alpha)
    {
        while (_pool.Count < shells.Length)
        {
            var tracer = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.12f, 3f) }, MaterialOverride = _material };
            AddChild(tracer);
            _pool.Add(tracer);
        }
        for (int i = 0; i < _pool.Count; i++)
        {
            MeshInstance3D view = _pool[i];
            view.Visible = i < shells.Length;
            if (!view.Visible) continue;
            Shell shell = shells[i];
            Vector3 position = (shell.Position - shell.Velocity * ((1f - alpha) * SimConstants.Dt)).ToGodot();
            Vector3 heading = shell.Velocity.ToGodot().Normalized();
            if (Mathf.Abs(heading.Y) < 0.99f) view.LookAtFromPosition(position, position + heading, Vector3.Up);
            else view.Position = position;
        }
    }
}
```

- [ ] **Step 2: 炮口焰与爆炸**

`src/Tank.Client/scripts/Combat/BattleFx.cs`：

```csharp
using Godot;

namespace Tank.Client.Combat;

/// <summary>Short-lived glowing flashes: a muzzle blast when firing, a fireball where a shell lands.</summary>
public partial class BattleFx : Node3D
{
    public void Muzzle(Vector3 at) => Flash(at, new Color(1f, 0.8f, 0.45f), 1.6f, 0.12f);

    public void Explosion(Vector3 at) => Flash(at, new Color(1f, 0.55f, 0.2f), 4.5f, 0.45f);

    private void Flash(Vector3 at, Color color, float radius, float seconds)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = color,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 3f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        var ball = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = material,
            Position = at,
            Scale = Vector3.One * (radius * 0.3f),
        };
        var light = new OmniLight3D { LightColor = color, LightEnergy = 4f, OmniRange = radius * 3f };
        ball.AddChild(light);
        AddChild(ball);

        Tween tween = ball.CreateTween();
        tween.TweenProperty(ball, "scale", Vector3.One * radius, seconds).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
        tween.Parallel().TweenProperty(material, "albedo_color", new Color(color, 0f), seconds);
        tween.Parallel().TweenProperty(light, "light_energy", 0f, seconds);
        tween.TweenCallback(Callable.From(ball.QueueFree));
    }
}
```

- [ ] **Step 3: 准星**

`src/Tank.Client/scripts/Overlay/Reticle.cs`：

```csharp
using Godot;

namespace Tank.Client.Overlay;

/// <summary>
/// Centre dot = where the player wants to aim; ring = where the gun would hit right now (with a reload arc).
/// When they overlap the gun is on target. A status line sits bottom-left.
/// </summary>
public partial class Reticle : Control
{
    private const float RingRadius = 16f;
    private const float ReloadRadius = 22f;
    private static readonly Color Ring = new(1f, 1f, 1f, 0.9f);
    private static readonly Color Dot = new(1f, 1f, 1f, 0.7f);
    private static readonly Color ReloadColor = new(1f, 0.72f, 0.18f);

    private Vector2? _impact;
    private float _reload = 1f;
    private string _status = "";

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <param name="impact">Screen position of the predicted hit, or null if it is behind the camera.</param>
    /// <param name="reloadProgress">0 just fired … 1 ready.</param>
    public void UpdateState(Vector2? impact, float reloadProgress, string status)
    {
        _impact = impact;
        _reload = Mathf.Clamp(reloadProgress, 0f, 1f);
        _status = status;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawCircle(Size / 2, 2.5f, Dot);
        if (_impact is { } p)
        {
            DrawArc(p, RingRadius, 0, Mathf.Tau, 40, Ring, 2f, true);
            if (_reload < 1f)
                DrawArc(p, ReloadRadius, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * _reload, 40, ReloadColor, 3f, true);
        }
        DrawString(ThemeDB.FallbackFont, new Vector2(24, Size.Y - 28), _status, HorizontalAlignment.Left, -1, 18, Colors.White);
    }
}
```

- [ ] **Step 4: 编译**

Run: `dotnet build src/Tank.Client/Tank.Client.csproj`
Expected: `Build succeeded`

- [ ] **Step 5: 提交**

```bash
git add src/Tank.Client/scripts/Combat src/Tank.Client/scripts/Overlay
git commit -m "feat(client): shell tracers, blast effects and reticle"
```

---

### Task 18：试驾场主循环与场景

**Files:**
- Modify: `src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs`
- Create: `src/Tank.Client/scripts/Sandbox/SandboxInput.cs`
- Create: `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`
- Create: `src/Tank.Client/scenes/Sandbox.tscn`

- [ ] **Step 1: 加 `--autodrive` 参数**

在 `src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs` 中：

1. 文档注释中 `--no-update（不检查在线更新）` 所在行末尾追加 ` --autodrive（试驾场：自动驾驶并开火，用于截图）`。
2. 在 `public bool UpdateNow { get; init; }` 下面加：
   ```csharp
       public bool AutoDrive { get; init; }
   ```
3. 在 `switch` 里 `"--update-now" => ...` 这一行下面加：
   ```csharp
                   "--autodrive" => o with { AutoDrive = true },
   ```

- [ ] **Step 2: 输入**

`src/Tank.Client/scripts/Sandbox/SandboxInput.cs`：

```csharp
using Godot;

namespace Tank.Client.Sandbox;

/// <summary>
/// Registers the sandbox's own input actions at runtime (so project.godot and the old game's bindings stay untouched)
/// and samples them once per sim tick. A fire click shorter than a tick is latched so it is never lost.
/// </summary>
public sealed class SandboxInput
{
    private bool _fireLatched;

    public static void RegisterActions()
    {
        AddKey("drive_forward", Key.W);
        AddKey("drive_back", Key.S);
        AddKey("drive_left", Key.A);
        AddKey("drive_right", Key.D);
        AddKey("sniper_toggle", Key.Shift);
        AddMouse("fire_gun", MouseButton.Left);
        AddMouse("free_look", MouseButton.Right);
    }

    public bool FreeLook => Input.IsActionPressed("free_look");

    public void OnInputEvent(InputEvent e)
    {
        if (e.IsActionPressed("fire_gun")) _fireLatched = true;
    }

    public (sbyte Throttle, sbyte Steer, bool Fire) Sample()
    {
        sbyte throttle = (sbyte)(Held("drive_forward") - Held("drive_back"));
        sbyte steer = (sbyte)(Held("drive_right") - Held("drive_left"));
        bool fire = _fireLatched || Input.IsActionPressed("fire_gun");
        _fireLatched = false;
        return (throttle, steer, fire);
    }

    private static int Held(string action) => Input.IsActionPressed(action) ? 1 : 0;

    private static void AddKey(string action, Key key) => Add(action, new InputEventKey { PhysicalKeycode = key });

    private static void AddMouse(string action, MouseButton button) => Add(action, new InputEventMouseButton { ButtonIndex = button });

    private static void Add(string action, InputEvent e)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, e);
    }
}
```

- [ ] **Step 3: 主循环**

`src/Tank.Client/scripts/Sandbox/SandboxMain.cs`：

```csharp
using Godot;
using Tank.Client.Bootstrap;
using Tank.Client.Cameras;
using Tank.Client.Combat;
using Tank.Client.Overlay;
using Tank.Client.Vehicles;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.Sandbox;

/// <summary>
/// Stage-1 offline test drive: runs Tank.Sim locally at 30Hz and renders one tick behind, interpolated.
/// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --autodrive --screenshot FILE --quit-after S]
/// </summary>
public partial class SandboxMain : Node3D
{
    private const double MaxCatchUp = 0.25;
    private const float AimRange = 900f;
    private const float SniperEyeAboveTrunnion = 0.45f;

    private readonly SandboxInput _input = new();
    private readonly InputCmd[] _commands = new InputCmd[1];
    private LaunchOptions _options = new();
    private Simulation _sim = null!;
    private TankState _previous;
    private TankState _current;
    private OrbitCamera _camera = null!;
    private TankModel _tank = null!;
    private ShellViews _shells = null!;
    private BattleFx _fx = null!;
    private Reticle _reticle = null!;
    private double _accumulator;
    private double _time;
    private float _aimYaw;
    private float _aimPitch;
    private bool _screenshotTaken;

    public override void _Ready()
    {
        _options = LaunchOptions.Parse(OS.GetCmdlineUserArgs());
        GetWindow().Title = "坦克 3D · 试驾场";
        SandboxInput.RegisterActions();

        _sim = new Simulation(SandboxMap.Create());
        _sim.AddTank(TankCatalog.Medium, SandboxMap.Spawn, 0f);
        _previous = _current = _sim.Tanks[0];
        _aimYaw = _current.Yaw;

        AddChild(Scenery.CreateEnvironment());
        AddChild(Scenery.CreateSun());
        var terrain = new TerrainMesh();
        AddChild(terrain);
        terrain.Build(_sim.Field.Terrain);
        var props = new PropMeshes();
        AddChild(props);
        props.Build(_sim.Field.Props);
        _tank = new TankModel();
        AddChild(_tank);
        _tank.Build(TankCatalog.Medium);
        _shells = new ShellViews();
        AddChild(_shells);
        _fx = new BattleFx();
        AddChild(_fx);
        _camera = new OrbitCamera();
        AddChild(_camera);
        _camera.SetAngles(_current.Yaw, 0.25f);
        var hud = new CanvasLayer();
        AddChild(hud);
        _reticle = new Reticle();
        hud.AddChild(_reticle);

        if (!_options.AutoDrive) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _Input(InputEvent e)
    {
        if (Input.MouseMode == Input.MouseModeEnum.Captured) _input.OnInputEvent(e);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        switch (e)
        {
            case InputEventMouseMotion motion when captured:
                _camera.Look(motion.Relative);
                break;
            case InputEventMouseButton { Pressed: true } wheel when captured && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                _camera.Scroll(wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
                break;
            case InputEventMouseButton { Pressed: true } when !captured:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;
        }
        if (e.IsActionPressed("sniper_toggle")) _camera.ToggleSniper();
        if (e.IsActionPressed("ui_cancel")) Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        _accumulator = Math.Min(_accumulator + delta, MaxCatchUp);
        while (_accumulator >= SimConstants.Dt)
        {
            _accumulator -= SimConstants.Dt;
            StepSimulation();
        }

        float alpha = (float)(_accumulator / SimConstants.Dt);
        TankState tank = Interpolate(_previous, _current, alpha);
        TankPose pose = TankPose.Compute(tank, _sim.Field.Terrain);
        _tank.Apply(pose, tank.TurretYaw, tank.GunPitch);
        _tank.Visible = !_camera.Sniper;
        if (_options.AutoDrive) _camera.SetAngles(tank.Yaw + 0.35f, 0.18f);
        _camera.Follow(pose.Position.ToGodot(), (pose.Trunnion(tank.Spec) + pose.Up * SniperEyeAboveTrunnion).ToGodot(), _sim.Field.Terrain);
        if (!_input.FreeLook) UpdateAim(pose, tank.Spec);
        _shells.Sync(_sim.Shells, alpha);
        UpdateReticle(pose, tank);
        RunAutomation();
    }

    private void StepSimulation()
    {
        var (throttle, steer, fire) = _options.AutoDrive ? AutoDrive() : _input.Sample();
        _commands[0] = new InputCmd(_sim.Tick + 1, throttle, steer, _aimYaw, _aimPitch, fire);
        _sim.Step(_commands);
        foreach (GameEvent e in _sim.Events)
        {
            if (e.Type == GameEventType.ShellFired) _fx.Muzzle(e.Position.ToGodot());
            else if (e.Type == GameEventType.ShellExploded) _fx.Explosion(e.Position.ToGodot());
        }
        _previous = _current;
        _current = _sim.Tanks[0];
    }

    private (sbyte Throttle, sbyte Steer, bool Fire) AutoDrive() => (1, (sbyte)(Math.Sin(_time * 0.4) > 0.5 ? 1 : 0), true);

    /// <summary>Casts from the orbit focus along the camera's view; the turret then chases that point.</summary>
    private void UpdateAim(TankPose pose, TankSpec spec)
    {
        Camera3D cam = _camera.Camera;
        SN.Vector3 look = (-cam.GlobalBasis.Z).ToSim();
        SN.Vector3 origin = cam.GlobalPosition.ToSim() + look * (_camera.Sniper ? 0f : _camera.Distance);
        SN.Vector3 end = origin + look * AimRange;
        SN.Vector3 target = _sim.Field.Raycast(origin, end, out RayHit hit) ? hit.Point : end;
        (_aimYaw, _aimPitch) = Angles.AimAngles(target - pose.Trunnion(spec));
    }

    private void UpdateReticle(TankPose pose, in TankState tank)
    {
        SN.Vector3 muzzle = pose.Muzzle(tank.Spec, tank.TurretYaw, tank.GunPitch);
        SN.Vector3 velocity = pose.GunDirection(tank.TurretYaw, tank.GunPitch) * tank.Spec.ShellSpeed;
        Ballistics.PredictImpact(muzzle, velocity, _sim.Field, out SN.Vector3 impact);
        Camera3D cam = _camera.Camera;
        Vector3 point = impact.ToGodot();
        Vector2? screen = cam.IsPositionBehind(point) ? null : cam.UnprojectPosition(point);
        string reload = tank.Reload > 0 ? $"{tank.Reload:0.0} 秒" : "就绪";
        string zoom = _camera.Sniper ? $"   狙击 {_camera.Zoom:0}×" : "";
        _reticle.UpdateState(screen, 1f - tank.Reload / tank.Spec.ReloadTime,
            $"速度 {MathF.Abs(tank.Speed) * 3.6f:0} km/h   装填 {reload}{zoom}");
    }

    private static TankState Interpolate(in TankState a, in TankState b, float t) => b with
    {
        Position = SN.Vector2.Lerp(a.Position, b.Position, t),
        Yaw = Angles.Lerp(a.Yaw, b.Yaw, t),
        TurretYaw = Angles.Lerp(a.TurretYaw, b.TurretYaw, t),
        GunPitch = a.GunPitch + (b.GunPitch - a.GunPitch) * t,
        Speed = a.Speed + (b.Speed - a.Speed) * t,
    };

    private void RunAutomation()
    {
        if (_options.ScreenshotPath is { } path && !_screenshotTaken && _time >= _options.ScreenshotAt)
        {
            _screenshotTaken = true;
            Error error = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print(error == Error.Ok ? $"Screenshot saved: {path}" : $"Screenshot failed ({error}): {path}");
        }
        if (_options.QuitAfter > 0 && _time >= _options.QuitAfter) GetTree().Quit();
    }
}
```

- [ ] **Step 4: 场景文件**

`src/Tank.Client/scenes/Sandbox.tscn`：

```
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://scripts/Sandbox/SandboxMain.cs" id="1"]

[node name="Sandbox" type="Node3D"]
script = ExtResource("1")
```

- [ ] **Step 5: 编译**

Run: `dotnet build src/Tank.Client/Tank.Client.csproj`
Expected: `Build succeeded`

- [ ] **Step 6: 首次导入并截图**

新 worktree 里还没有 `.godot/` 导入缓存，先导入一次，再用自动驾驶截两张图：

```bash
godot-mono --headless --path src/Tank.Client --import
mkdir -p build
godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn -- --autodrive --screenshot "$PWD/build/sandbox-3s.png" --screenshot-at 3 --quit-after 4
godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn -- --autodrive --screenshot "$PWD/build/sandbox-12s.png" --screenshot-at 12 --quit-after 13
```

Expected: 输出 `Screenshot saved: …`。用 Read 工具查看两张 PNG：
- 3 秒：天空、起伏的绿色地形、远处的山、灰色岩石；画面中下方是坦克，炮管朝前；准星圆环在地面上，状态栏有速度和装填时间。
- 12 秒：坦克已经开出一段距离，车身随地形倾斜；画面里能看到爆炸火球或曳光。

如果截图全黑或没有地形，先看终端里有没有 C# 异常；常见原因是 `Build()` 在 `AddChild()` 之前调用，或者镜头的 `Current` 没生效。

`build/` 已在 `.gitignore` 中，截图不会被提交。

- [ ] **Step 7: 提交**

```bash
git add src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs src/Tank.Client/scripts/Sandbox src/Tank.Client/scenes/Sandbox.tscn
git commit -m "feat(client): stage 1 sandbox scene with third-person driving and shooting"
```

---

### Task 19：手感验收与收尾

**Files:**
- 视情况修改：`src/Tank.Sim/Vehicles/TankCatalog.cs`、`src/Tank.Client/scripts/Cameras/OrbitCamera.cs`

- [ ] **Step 1: 全部测试**

```bash
dotnet test tests/Tank.Sim.Tests
dotnet test tests/Tank.Core.Tests
dotnet test tests/Tank.Net.Tests
dotnet build src/Tank.Client/Tank.Client.csproj
```

Expected: 全部通过，旧项目的测试不受影响。

- [ ] **Step 2: 交给用户试玩**

请用户运行：

```bash
godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn
```

并逐项确认：

| # | 检查项 |
|---|---|
| 1 | WASD 能前进、倒车、原地转向；松开 W 后会刹停 |
| 2 | 能开上北边的大山；遇到陡坡（岩石色的地面）会爬不上去 |
| 3 | 车身随地形前后、左右倾斜，没有明显陷进地里或悬空 |
| 4 | 鼠标转动镜头，炮塔会按有限速度追上准星；车体转向时炮塔仍指向原来的目标 |
| 5 | 中心点和圆环重合时开炮，炮弹落在圆环处 |
| 6 | 左键开炮有炮口焰、曳光和落点爆炸；装填期间圆环外有橙色进度弧 |
| 7 | Shift 进入狙击镜，滚轮在 2×/4×/8× 之间切换；再按 Shift 退出 |
| 8 | 滚轮在第三人称下拉近、拉远；镜头不会穿进地形 |
| 9 | 撞到岩石会停下；斜着撞会贴边滑行 |
| 10 | 右键按住时可以自由环顾，炮塔不跟随 |

- [ ] **Step 3: 根据反馈微调**

手感问题只改参数，不改结构：速度和加速度改 `TankCatalog.Medium`，镜头距离和灵敏度改 `OrbitCamera` 里的常量。每次调整后重跑 `dotnet test tests/Tank.Sim.Tests`；如果某个测试是按旧参数写死的期望值（例如 `SandboxMapTests` 的山高），同步修改。

- [ ] **Step 4: 提交**

```bash
git add -A src/Tank.Sim src/Tank.Client/scripts
git commit -m "tune: stage 1 handling after playtest"
```

（没有调整就跳过这一步。）

- [ ] **Step 5: 合并**

使用 superpowers:finishing-a-development-branch 决定合并方式。
