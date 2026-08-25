namespace ZStudio.CameraScaler {
    /// <summary>将适配结果写入 Camera 的时机。</summary>
    public enum EApplyTiming {
        /// <summary>在 Update 中写入。适合大多数项目，并能在 Start 中读到首次适配结果。</summary>
        Update,

        /// <summary>在 LateUpdate 中写入。适合覆盖普通相机控制脚本。</summary>
        LateUpdate,

        /// <summary>在 OnPreCull 中写入。适合覆盖 Cinemachine 等在渲染前才定稿的系统。</summary>
        OnPreCull
    }
}
