using Unity.Logging;
using UnityEngine;

/// <summary>
///     シングルトンパターンの基底クラス
/// </summary>
/// <typeparam name="T"></typeparam>
public class Singleton<T> where T : class, new()
{
    private static T _instance;
    private static readonly object _lock = new();

#if UNITY_EDITOR
    static Singleton()
    {
        // [RuntimeInitializeOnLoadMethod] はジェネリッククラス内のメソッドに付与できないため、
        // ドメインリロード時のリセット処理は非ジェネリックな SingletonDomainResetRegistry に委譲する。
        // 静的コンストラクタは閉じた型（Singleton<T> の T ごと）に1度だけ実行されるため、
        // 実際に使用された型のみ登録される。
        SingletonDomainResetRegistry.Register(() =>
        {
            Log.Debug("[Singleton<" + typeof(T).Name + ">] DomainReset called.");
            _instance = null;
        });
    }
#endif

    public static T Instance
    {
        get
        {
            if (_instance != null)
            {
                return _instance;
            }

            lock (_lock)
            {
                _instance ??= new T();
            }
            return _instance;
        }
    }
}