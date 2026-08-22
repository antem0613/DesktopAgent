using UnityEngine;
using UnityEngine.Serialization;

namespace LeTai.Asset.TranslucentImage.Demo
{
[RequireComponent(typeof(RectTransform))]
public class DemoEffectBreathing : MonoBehaviour
{
    [SerializeField] Vector2 amplitudePercent = new(5, 5);
    [SerializeField] Vector2 speed            = new(1, 1);
    [SerializeField] float   phaseOffset      = .1f;

    RectTransform _rectTransform;
    Vector2       _baseSize;

    void OnEnable()
    {
        _rectTransform = (RectTransform)transform;
        _baseSize      = _rectTransform.sizeDelta;
    }

    void OnDisable()
    {
        if (_rectTransform)
            _rectTransform.sizeDelta = _baseSize;
    }

    void Update()
    {
        var t = Time.time;
        var a = Mathf.Min(_baseSize.x, _baseSize.y) * .01f * amplitudePercent;
        var offset = new Vector2(
            a.x * Mathf.Sin(Mathf.PI * 2 * (t * speed.x)),
            a.y * Mathf.Sin(Mathf.PI * 2 * (t * speed.y + phaseOffset))
        );

        var size = _baseSize + offset;
        if (_rectTransform.sizeDelta != size)
            _rectTransform.sizeDelta = size;
    }
}
}
