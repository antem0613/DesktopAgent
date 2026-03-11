using UnityEngine;

public static class ScreenSpaceTransformUtility
{
    public static bool TryGetScreenPosition(Camera camera, Transform target, out Vector3 screenPosition)
    {
        screenPosition = default;
        if (camera == null || target == null)
        {
            return false;
        }

        screenPosition = camera.WorldToScreenPoint(target.position);
        return screenPosition.z > 0f;
    }

    public static bool TrySetScreenPosition(Camera camera, Transform target, Vector3 screenPosition, float depth)
    {
        if (camera == null || target == null)
        {
            return false;
        }

        target.position = camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, depth));
        return true;
    }
}