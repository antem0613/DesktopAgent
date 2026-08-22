// Copyright (c) Le Loc Tai <leloctai.com> . All rights reserved. Do not redistribute.

using System;
using UnityEngine;

namespace LeTai.Common
{
public static class SpanUtils
{
    public static Vector4 ToVector4(ReadOnlySpan<float> span)
    {
        return new Vector4(span[0], span[1], span[2], span[3]);
    }

    public static SpanWriter<T> WriterFor<T>(Span<T> span) => new(span);
}

public ref struct SpanWriter<T>
{
    readonly Span<T> _span;
    int              _nextIndex;

    public SpanWriter(Span<T> span)
    {
        _span      = span;
        _nextIndex = 0;
    }

    public void Reset() => _nextIndex = 0;

    public void Write(T value)
    {
        _span[_nextIndex++] = value;
    }

    public void FillRest(T value = default)
    {
        if (_nextIndex == _span.Length)
            return;

        _span[_nextIndex..].Fill(value);
        _nextIndex = _span.Length;
    }
}

/// <summary>
/// Why:
///  - Don't want ext method. Pollutes everything.
///  - Immediate values are not semantically float/vector2/3
/// </summary>
public readonly struct Vec4Builder
{
    public static Vec4Part1 Append(float x) => new(x);
}

public readonly struct Vec4Part1
{
    readonly float _x;
    public Vec4Part1(float        x) => _x = x;
    public Vec4Part2 Append(float y) => new(_x, y);
}

public readonly struct Vec4Part2
{
    readonly float _x, _y;

    public Vec4Part2(float x, float y)
    {
        _x = x;
        _y = y;
    }

    public Vec4Part3 Append(float z) => new(_x, _y, z);
}

public readonly struct Vec4Part3
{
    readonly float _x, _y, _z;

    public Vec4Part3(float x, float y, float z)
    {
        _x = x;
        _y = y;
        _z = z;
    }

    public Vector4 Append(float w) => new(_x, _y, _z, w);
}
}
