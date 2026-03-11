using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 複数のIWindowRectProviderを合成してRect一覧を返す。
/// </summary>
public sealed class CompositeWindowRectProvider : IWindowRectProvider, IWindowRectProviderWithSource
{
    private readonly List<IWindowRectProvider> _providers;

    public CompositeWindowRectProvider(IEnumerable<IWindowRectProvider> providers)
    {
        _providers = providers != null ? new List<IWindowRectProvider>(providers) : new List<IWindowRectProvider>();
    }

    public List<Rect> GetWindowRects()
    {
        var result = new List<Rect>();

        foreach (var provider in _providers)
        {
            if (provider == null)
            {
                continue;
            }

            var rects = provider.GetWindowRects();
            if (rects == null || rects.Count == 0)
            {
                continue;
            }

            result.AddRange(rects);
        }

        return result;
    }

    public List<WindowRectWithSource> GetWindowRectsWithSource()
    {
        var result = new List<WindowRectWithSource>();

        foreach (var provider in _providers)
        {
            if (provider == null)
            {
                continue;
            }

            if (provider is IWindowRectProviderWithSource withSource)
            {
                var rects = withSource.GetWindowRectsWithSource();
                if (rects != null && rects.Count > 0)
                {
                    result.AddRange(rects);
                }
            } else
            {
                // sourceが取れないプロバイダはSource=nullとして追加
                var rects = provider.GetWindowRects();
                if (rects == null || rects.Count == 0)
                {
                    continue;
                }

                foreach (var rect in rects)
                {
                    result.Add(new WindowRectWithSource(rect, null));
                }
            }
        }

        return result;
    }
}
