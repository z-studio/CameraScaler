# Camera Scaler

Camera Scaler 用于让 Unity 相机像 `CanvasScaler` 一样，根据参考分辨率和实际屏幕宽高比调整可视范围。它同时支持正交相机和透视相机，可用于横竖屏适配、异形比例设备以及需要固定水平视野的游戏。

## 安装

1. 从 `Window > Package Manager` 打开 `Package Manager`
2. 选择 `+ > Add package from git URL...`
3. 输入以下命令进行安装
    * https://github.com/z-studio/CameraScaler.git?path=Assets/CameraScaler

<p align="center">
  <img width="80%" src="https://user-images.githubusercontent.com/47441314/118421190-97842b00-b6fb-11eb-9f94-4dc94e82367a.png" alt="Package Manager">
</p>

或者，打开 `Packages/manifest.json` 文件，并将以下内容添加到 dependencies 块中：

```json
{
    "dependencies": {
        "com.zstudio.camera-scaler": "https://github.com/z-studio/CameraScaler.git?path=Assets/CameraScaler"
    }
}
```

如果要设置目标版本，请按如下方式指定：

* https://github.com/z-studio/CameraScaler.git?path=Assets/CameraScaler#v1.0.0

## 快速开始

1. 在带有 `Camera` 的 GameObject 上添加 `Layout/ZStudio/Camera Scaler`。
2. 将 `Reference Resolution` 设置为设计内容时使用的分辨率，例如 `1080 × 1920`。
3. 确认 `Reference Orthographic Size` / `Reference Field Of View` 等于你在参考分辨率下的设计值。添加组件时会从 Camera 自动采集；之后请改组件上的基准，而不是只改 Camera。
4. 根据游戏的画面策略选择 `Scale Mode`。
5. 进入 Play Mode，切换 Game View 的宽高比检查画面边界。

组件把设计基准保存在自身序列化字段中，而不是在 `Awake` 里偷偷记住 Camera 的瞬时值。Play Mode 下首次适配在 `OnEnable` 完成，因此其他组件可在 `Start` 中读取适配后的相机。后续适配始终以这组基准值为输入，不会产生逐帧累积误差。Edit Mode 不会把适配结果写回 Camera，以免把设计 Size/FOV 存进场景。

## 五种适配模式

| 模式 | 行为 | 常见用途 |
|---|---|---|
| `ConstantHeight` | 保持垂直可视范围 | Unity 默认相机行为、纵向视野必须固定 |
| `ConstantWidth` | 保持水平可视范围 | 竖屏游戏、横向玩法边界必须固定 |
| `MatchWidthOrHeight` | 在固定宽度和固定高度之间按权重插值 | 需要和 CanvasScaler 的 Match 策略一致 |
| `Expand` | 保证参考区域全部可见，设备多出的空间显示额外内容 | 不能裁掉玩法区域 |
| `Shrink` | 不显示参考区域之外的内容，必要时裁减参考区域 | 不允许看到关卡边界之外 |

`MatchWidthOrHeight` 的权重为 `0` 时等同 `ConstantWidth`，为 `1` 时等同 `ConstantHeight`。正交和透视模式均在投影尺度的对数空间插值。

## 运行时 API

```csharp
using UnityEngine;
using ZStudio.CameraScaler;

public sealed class CameraController : MonoBehaviour {
    [SerializeField] private CameraScaler m_Scaler;

    private void Start() {
        // 2 倍放大。正交相机会将 Size 缩小一半；
        // 透视相机会按投影平面尺度做等价缩放。
        m_Scaler.CameraZoom = 2f;

        // 运行时修改适配配置会立即刷新相机。
        m_Scaler.ReferenceResolution = new Vector2(1080f, 1920f);
        m_Scaler.ScaleMode = EScaleMode.Expand;
        m_Scaler.MatchWidthOrHeight = 0.5f;
        m_Scaler.ApplyTiming = EApplyTiming.OnPreCull;
    }
}
```

可用成员：

- `ReferenceResolution`：当前参考分辨率；宽高非法时会被修正为 `1`。
- `ScaleMode`：当前适配模式，类型为 `EScaleMode`。
- `MatchWidthOrHeight`：宽高匹配权重，自动限制在 `0～1`。
- `ReferenceOrthographicSize`：参考分辨率下的正交垂直半尺寸。
- `ReferenceFieldOfView`：参考分辨率下的透视垂直视野角。
- `CameraZoom`：缩放倍率，必须是大于 `0` 的有限值；非法输入会被拒绝。可在 Inspector 中序列化。
- `ApplyTiming`：将结果写入 Camera 的时机（`EApplyTiming.Update` / `LateUpdate` / `OnPreCull`）。
- `PreviewInEditMode`：编辑模式自动预览开关，默认关闭，不影响运行时适配。
- `HorizontalSize`：参考分辨率下、未应用 Zoom 的正交水平半尺寸。
- `HorizontalFov`：参考分辨率下、未应用 Zoom 的水平视野角。
- `Refresh()`：外部直接修改 Camera 配置后，强制重新应用适配。不会重新采集基准。
- `RecaptureBaseline()`：把 Camera 当前的 Size/FOV 采集为新基准。Play Mode 下会立即重新应用适配。

纯计算逻辑在 `CameraScalerMath` 中，不依赖组件生命周期，便于测试或给其他相机系统复用。

## Zoom 的正确用法

Camera Scaler 会接管以下属性：

- 正交相机：`Camera.orthographicSize`
- 透视相机：`Camera.fieldOfView`

运行时不要直接修改它们来实现缩放，应修改 `CameraZoom`。透视相机的角度不是线性尺度，因此组件会按照 `tan(FOV / 2)` 所表示的投影平面尺度计算 Zoom；这比直接执行 `FOV / Zoom` 更准确。

如果其他系统必须直接修改相机参数，应先明确新的基准值需求：

- 只想按当前宽高比重新套用已有基准：调用 `Refresh()`。
- 想把 Camera 上刚设好的 Size/FOV 当作新的设计值：调用 `RecaptureBaseline()`。

## 生命周期和动态变化

- Play Mode 下首次适配在 `OnEnable` 完成，因此其他组件可在 `Start` 中读取适配后的相机。Edit Mode 默认不自动适配；开启 `Preview In Edit Mode` 后可实时预览。
- 屏幕宽高比、工作模式、Match 权重、参考分辨率、参考 Size/FOV、Zoom 以及正交/透视切换都会自动触发刷新。
- 在 Camera Scaler 自身初始化之前设置公开属性是安全的；配置会在启用时应用。
- Unity 对象不是线程安全的，所有 API 必须在主线程调用。
- 禁用组件期间修改 Camera 后，重新启用组件会重新应用适配。
- 同一物体不能挂多个 Camera Scaler。

## 编辑模式预览

在 Inspector 勾选 `Preview In Edit Mode`，或设置 `PreviewInEditMode = true`，即可在不点击 Play 时预览适配。修改 Game 窗口比例、适配参数或相机投影类型都会自动刷新，无需保持选中组件，也不受 `Apply Timing` 影响。

关闭预览、禁用或移除组件时，会恢复预览前的 Camera Size/FOV。保存场景和进入 Play 前也会恢复，避免将预览结果保存为设计参数；返回编辑模式后继续预览。参考 Size/FOV 始终作为计算基准，不会随预览反复改变。预览期间调用 `RecaptureBaseline()` 会先恢复原相机参数，再采集并恢复预览；如需采集手动调整的 Camera 参数，请先关闭预览。

## 参数保护

- 参考分辨率的宽、高必须大于 `0`；Inspector 和运行时 API 都会修正非法值。
- `CameraZoom` 必须大于 `0`，且不能是 `NaN` 或无穷大。
- Match 权重会限制在 `0～1`。
- 透视 FOV 的最终结果会限制在 Unity 可用的 `1°～179°`。
- 相机宽高比异常时会临时回退到参考分辨率的宽高比；极端比例会限制在安全计算范围内，避免除零、溢出和非法投影。

## 与其他相机系统配合

- Built-in、URP 和 HDRP 均使用 Unity `Camera` 的 Size/FOV，因此适配算法本身不依赖渲染管线。
- Cinemachine 或自定义相机控制器也可能在每帧写入 Size/FOV。应当只保留一个最终写入者。若这些系统在 `LateUpdate` 或渲染前才定稿，把 `Apply Timing` 改为 `Late Update` 或 `On Pre Cull`。
- 启用 Physical Camera 时，FOV 与焦距/传感器尺寸互相派生。建议关闭 Physical Camera，或确保没有其他系统同时写入这些属性。
- 多相机项目应在每个需要独立适配的 Camera 上分别添加组件并配置参考分辨率。

## 上线前检查

- 覆盖项目支持的最窄、最宽、横屏和竖屏比例。
- 分别验证实际使用的正交/透视模式和全部工作模式。
- 检查相机堆叠、Cinemachine、震屏、Zoom 动画是否争抢 Size/FOV。
- 在刘海屏或挖孔屏项目中单独处理 Safe Area；Camera Scaler 只解决宽高比，不负责 UI 安全区。
- 在目标 Unity 版本和目标平台执行 PlayMode 测试与真机测试。
