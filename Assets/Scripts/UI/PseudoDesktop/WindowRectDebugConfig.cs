using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "DesktopAgent/Window Rect Debug Config", fileName = "WindowRectDebugConfig")]
public class WindowRectDebugConfig : ScriptableObject
{
    [Header("Editor Mock")]
    public bool useMockInEditor = true;

    [Tooltip("Unityのスクリーン座標(左下原点, px)で指定します")]
    public List<Rect> mockWindowRects = new List<Rect>
        {
            // 例: 画面下部にあるウィンドウ
            new Rect(200, 200, 900, 500),
        };

    [Header("Overlay")]
    public bool drawOverlayInEditor = true;
    public Color overlayColor = new Color(0f, 0.7f, 1f, 0.25f);
    public Color borderColor = new Color(0f, 0.7f, 1f, 0.9f);
}