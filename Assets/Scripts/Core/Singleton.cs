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
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void DomainReset()
    {
        Log.Debug("[Singleton<" + typeof(T).Name + ">] DomainReset called.");
        _instance = null;
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