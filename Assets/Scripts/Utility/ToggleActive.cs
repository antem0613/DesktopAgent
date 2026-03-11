using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class ToggleActive : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    bool _isActive = true;
    [SerializeField] bool startActive = true;
    [SerializeField] bool deactivateOnClickOutside = false;
    [SerializeField] InputActionAsset inputActions;
    InputAction _clickAction;
    bool _isMouseOver = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _isActive = startActive;
        gameObject.SetActive(_isActive);
        _clickAction = inputActions.FindAction("Click");
    }

    // Update is called once per frame
    void Update()
    {
        if (deactivateOnClickOutside && _isActive && _clickAction.triggered)
        {
            if (!_isMouseOver)
            {
                _isActive = false;
                gameObject.SetActive(_isActive);
            }
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_isActive) return;
        _isMouseOver = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_isActive) return;
        _isMouseOver = false;
    }

    public void Toggle()
    {
        _isActive = !_isActive;
        gameObject.SetActive(_isActive);
    }
}
