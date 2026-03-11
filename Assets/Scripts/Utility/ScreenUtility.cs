using UnityEngine;

/// <summary>
///     画面座標系に関するユーティリティクラス
/// </summary>
public static class ScreenUtility
{
    public static Vector2 GetModelScreenPosition(Camera mainCamera, Transform modelTransform)
    {
        Vector3 screenPos = mainCamera.WorldToScreenPoint(modelTransform.position);
        // Windowsのスクリーン座標系に合わせるため、Y座標を反転
        return new Vector2(screenPos.x, screenPos.y);
    }
}