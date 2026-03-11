using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// シーン上のCanvasWindowRect(RectTransform)からRectを取得するプロバイダ。
/// </summary>
public sealed class RectTransformWindowRectProvider : IWindowRectProvider, IWindowRectProviderWithSource
{
    public List<Rect> GetWindowRects()
    {
        var result = new List<Rect>();

        var panels = Object.FindObjectsByType<CanvasWindowRect>(FindObjectsSortMode.None);
        foreach (var panel in panels)
        {
            if (panel == null || !panel.IsEnabled)
            {
                continue;
            }

            if (panel.TryGetScreenRect(out var rect))
            {
                result.Add(rect);
            }
        }

        return result;
    }

    public List<WindowRectWithSource> GetWindowRectsWithSource()
    {
        var result = new List<WindowRectWithSource>();

        var panels = Object.FindObjectsByType<CanvasWindowRect>(FindObjectsSortMode.None);
        foreach (var panel in panels)
        {
            if (panel == null || !panel.IsEnabled)
            {
                continue;
            }

            if (panel.TryGetScreenRect(out var rect))
            {
                result.Add(new WindowRectWithSource(rect, panel));
            }
        }

        return result;
    }
}
