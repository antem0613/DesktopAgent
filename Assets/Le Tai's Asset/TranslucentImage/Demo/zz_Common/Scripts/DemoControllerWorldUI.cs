#if HAS_URP
using UnityEngine.Rendering.Universal;
#endif
#if ENABLE_INPUT_SYSTEM
using Pointer = UnityEngine.InputSystem.Pointer;
#endif
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace LeTai.Asset.TranslucentImage.Demo
{
public class DemoControllerWorldUI : MonoBehaviour
{
    public float  rotationSpeed = 1f;
    public Camera uiCamera;

    bool  isDragging;
    float lastMouseX;

    public void SetUseDepth(bool useDepth)
    {
#if HAS_URP
        if (GraphicsSettings.currentRenderPipeline != null)
        {
            var data = uiCamera.GetUniversalAdditionalCameraData();
            var field = typeof(UniversalAdditionalCameraData).GetField("m_ClearDepth",
                                                                       BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(data, !useDepth);
        }
#endif
        uiCamera.clearFlags = useDepth ? CameraClearFlags.Nothing : CameraClearFlags.Depth;
    }

    void Update()
    {
        bool  pressed, released;
        float mouseX;

#if ENABLE_INPUT_SYSTEM
        var pointer = Pointer.current;
        if (pointer == null)
            return;

        pressed  = pointer.press.wasPressedThisFrame;
        released = pointer.press.wasReleasedThisFrame;
        mouseX   = pointer.position.ReadValue().x;
#else
        pressed = Input.GetMouseButtonDown(0);
        released = Input.GetMouseButtonUp(0);
        mouseX = Input.mousePosition.x;
#endif

        if (pressed)
        {
            isDragging = true;
            lastMouseX = mouseX;
        }
        else if (released)
        {
            isDragging = false;
        }

        if (isDragging)
        {
            float delta = mouseX - lastMouseX;
            transform.Rotate(0, delta * rotationSpeed, 0, Space.World);
            lastMouseX = mouseX;
        }
    }
}
}
