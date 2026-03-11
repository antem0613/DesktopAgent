using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rectと、それを提供したソース(例: CanvasWindowRectコンポーネントやウィンドウハンドル)をセットで扱うためのデータ。
/// </summary>
public readonly struct WindowRectWithSource
{
    public readonly Rect Rect;
    public readonly object Source;

    public WindowRectWithSource(Rect rect, object source)
    {
        Rect = rect;
        Source = source;
    }
}

/// <summary>
/// Rectだけでなく「どのウィンドウか」を識別できる情報も提供できるプロバイダ。
/// </summary>
public interface IWindowRectProviderWithSource
{
    List<WindowRectWithSource> GetWindowRectsWithSource();
}
