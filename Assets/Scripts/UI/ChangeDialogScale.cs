using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class ChangeDialogScale : MonoBehaviour
{
    [SerializeField] GameObject root;
    RectTransform rectTransform;
    Vector2 original;

    [SerializeField] float minPercent, maxPercent;

    void Start()
    {
        rectTransform = root.GetComponent<RectTransform>();
        original = rectTransform.rect.size;
        Debug.Log(original);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void OnValueChanged(float value)
    {
        float scale = Mathf.Clamp(value, minPercent, maxPercent);
        Debug.Log(original * scale + ", " + value + ", " + scale);
        rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, original.x * scale);
        rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, original.y * scale);
    }
}
