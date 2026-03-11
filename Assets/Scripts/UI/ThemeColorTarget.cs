using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LeTai.TrueShadow;

public sealed class ThemeColorTarget : MonoBehaviour
{
    public enum TargetKind
    {
        PrimaryGraphic,
        SecondaryGraphic,
        TextGraphic,
        TrueShadow
    }

    [SerializeField] private TargetKind targetKind = TargetKind.PrimaryGraphic;

    private Graphic _graphic;
    private TMP_Text _tmpText;
    private TrueShadow _shadow;

    private void Awake()
    {
        CacheComponents();
    }

    private void OnEnable()
    {
        Register();
    }

    private void OnDisable()
    {
        Unregister();
    }

    private void CacheComponents()
    {
        _graphic = GetComponent<Graphic>();
        _tmpText = GetComponent<TMP_Text>();
        _shadow = GetComponent<TrueShadow>();
    }

    private void Register()
    {
        var binding = ThemeColorBinding.Instance;
        if (binding == null)
        {
            Debug.LogWarning("ThemeColorTarget: No ThemeColorBinding instance found in the scene.", this);
            return;
        }

        switch (targetKind)
        {
            case TargetKind.PrimaryGraphic:
                if (_graphic == null)
                {
                    Debug.LogWarning("PrimaryColorTarget: Graphic component not found.", this);
                    return;
                }

                binding.RegisterPrimaryGraphic(_graphic);
                break;
            case TargetKind.SecondaryGraphic:
                if (_graphic == null)
                {
                    Debug.LogWarning("SecondaryColorTarget: Graphic component not found.", this);
                    return;
                }

                binding.RegisterSecondaryGraphic(_graphic);
                break;
            case TargetKind.TextGraphic:
                if (_tmpText != null)
                {
                    binding.RegisterTextTMP(_tmpText);
                    break;
                }

                if (_graphic == null)
                {
                    Debug.LogWarning("ThemeColorTarget: Text target requires TMP_Text or Graphic.", this);
                    return;
                }

                binding.RegisterTextGraphic(_graphic);
                break;
            case TargetKind.TrueShadow:
                if (_shadow == null)
                {
                    Debug.LogWarning("ThemeColorTarget: TrueShadow component not found.", this);
                    return;
                }

                binding.RegisterShadow(_shadow);
                break;
            default:
                Debug.LogWarning("ThemeColorTarget: Unknown target kind.", this);
                break;
        }

    }

    private void Unregister()
    {
        var binding = ThemeColorBinding.Instance;
        if (binding == null)
        {
            return;
        }

        switch (targetKind)
        {
            case TargetKind.PrimaryGraphic:
                if (_graphic != null)
                {
                    binding.UnregisterPrimaryGraphic(_graphic);
                }
                break;
            case TargetKind.SecondaryGraphic:
                if (_graphic != null)
                {
                    binding.UnregisterSecondaryGraphic(_graphic);
                }
                break;
            case TargetKind.TextGraphic:
                if (_tmpText != null)
                {
                    binding.UnregisterTextTMP(_tmpText);
                }
                else if (_graphic != null)
                {
                    binding.UnregisterTextGraphic(_graphic);
                }
                break;
            case TargetKind.TrueShadow:
                if (_shadow != null)
                {
                    binding.UnregisterShadow(_shadow);
                }
                break;
        }

    }
}
