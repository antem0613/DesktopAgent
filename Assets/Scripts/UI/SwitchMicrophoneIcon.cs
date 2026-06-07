using UnityEngine;
using UnityEngine.UI;

/// <summary>
///   マイクのオンオフを切り替えるアイコン
/// </summary>
public class SwitchMicrophoneIcon : MonoBehaviour
{
    [SerializeField] private Sprite microphoneOnIcon;
    [SerializeField] private Sprite microphoneOffIcon;
    [SerializeField] private Sprite textInputMarkSprite;

    [SerializeField] private Image microphoneImage;

    private bool _isMicrophoneOn;
    private bool _isTextInputMarkVisible;

    /// <summary>
    ///  マイクのアイコンを切り替える
    /// </summary>
    /// <param name="isMicrophoneOn"></param>
    public void SwitchIcon(bool isMicrophoneOn)
    {
        _isMicrophoneOn = isMicrophoneOn;
        RefreshSprite();
    }

    public void SetTextInputMarkVisible(bool isVisible)
    {
        _isTextInputMarkVisible = isVisible;
        RefreshSprite();
    }

    private void RefreshSprite()
    {
        if (microphoneImage == null)
        {
            return;
        }

        if (_isTextInputMarkVisible && textInputMarkSprite != null)
        {
            microphoneImage.sprite = textInputMarkSprite;
            return;
        }

        microphoneImage.sprite = _isMicrophoneOn ? microphoneOnIcon : microphoneOffIcon;
    }
}