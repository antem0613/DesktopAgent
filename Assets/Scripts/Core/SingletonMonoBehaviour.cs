using Unity.Logging;
using UnityEngine;

/// <summary>
///     シーンにまたがってデータを保持するクラスのベースクラス
/// </summary>
/// <typeparam name="T"></typeparam>
public class SingletonMonoBehaviour<T> : MonoBehaviour where T : MonoBehaviour
{
    /// <summary>
    ///     シングルトンのインスタンス
    /// </summary>
    private static T _instance;

    /// <summary>
    ///     シングルトンのインスタンス
    /// </summary>
    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<T>();
                if (_instance == null)
                {
                    Log.Warning(typeof(T) + "SingletonMonoBehaviour is nothing");
                } else
                {

                    if (_instance.transform.parent != null)
                    {
                        _instance.transform.SetParent(null);
                    }
                }
            }

            return _instance;
        }
    }

    private protected virtual void Awake()
    {
        if (_instance == null)
        {
            _instance = this as T;

            if (transform.parent != null)
            {
                transform.SetParent(null);
            }
        } else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }
}