using System;
using System.Collections.Generic;
using Unity.Logging;
using UnityEngine;

#if UNITY_EDITOR
/// <summary>
///     Singleton&lt;T&gt; の各閉じた型（T ごと）が保持する静的インスタンスを、
///     ドメインリロード時に一括でリセットするための非ジェネリックなレジストリ。
///     Unity の [RuntimeInitializeOnLoadMethod] はジェネリッククラス内のメソッドには付与できないため、
///     実際のリセット処理はこの非ジェネリッククラス側に集約する。
///     各 Singleton&lt;T&gt; は静的コンストラクタでリセット用デリゲートを登録する。
/// </summary>
internal static class SingletonDomainResetRegistry
{
    private static readonly List<Action> _resetActions = new();
    private static readonly object _lock = new();

    /// <summary>ドメインリロード時に呼び出すリセット処理を登録する。</summary>
    public static void Register(Action resetAction)
    {
        if (resetAction == null) return;

        lock (_lock)
        {
            _resetActions.Add(resetAction);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void DomainReset()
    {
        Log.Debug("[SingletonDomainResetRegistry] DomainReset called.");

        Action[] actionsSnapshot;
        lock (_lock)
        {
            actionsSnapshot = _resetActions.ToArray();
        }

        foreach (var action in actionsSnapshot)
        {
            action?.Invoke();
        }
    }
}
#endif
