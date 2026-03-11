using UnityEngine;

public class WindowClose: MonoBehaviour
{
    [SerializeField] GameObject windowObject;

    public void CloseWindow()
    {
        Destroy(windowObject);
    }
}
