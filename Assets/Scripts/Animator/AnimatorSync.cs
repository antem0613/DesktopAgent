using Unity.Logging;
using UnityEngine;

public sealed class AnimatorSync
{
    Animator _animator;

    public AnimatorSync(Animator animator)
    {
        _animator = animator;
    }

    public void SetNextGroundAction(CharacterManager.GroundAction action)
    {
        if (_animator.GetBool(Constant.AnimatorIsOnWindow))
        {
            ClearGroundActionFlags();
            return;
        }

        Debug.Log("[AnimatorSync] SetNextGroundAction: " + action);
        SetBool(Constant.AnimatorIsIdling, action == CharacterManager.GroundAction.Idle);
        SetBool(Constant.AnimatorIsWalking, action == CharacterManager.GroundAction.Walk);
        SetBool(Constant.AnimatorIsSitting, action == CharacterManager.GroundAction.Sit);
    }

    public void SetFalling(bool value)
    {
        // IsFalling は地面行動フラグと独立。
        if (_animator.GetBool(Constant.AnimatorIsFalling) == value)
        {
            return;
        }

        _animator.SetBool(Constant.AnimatorIsFalling, value);
    }

    public void SetDragging(bool value)
    {
        // IsDragging はドラッグ入力で制御。
        SetBool(Constant.AnimatorIsDragging, value);

        if (!value)
        {
            return;
        }
        SetBool(Constant.AnimatorIsIdling, false);
        SetBool(Constant.AnimatorIsWalking, false);
        SetBool(Constant.AnimatorIsSitting, false);
        SetBool(Constant.AnimatorIsSittingWindow, false);
    }

    public void SetShouldGoNext(bool value)
    {
        if (_animator.GetBool(Constant.AnimatorShouldGoNext) == value)
        {
            return;
        }

        _animator.SetBool(Constant.AnimatorShouldGoNext, value);
    }

    public void SetIsOnWindow(bool value)
    {
        SetBool(Constant.AnimatorIsOnWindow, value);
        if (value)
        {
            ClearGroundActionFlags();
        }
    }

    public void SetIsSittingWindow(bool value)
    {
        SetBool(Constant.AnimatorIsSittingWindow, value);
    }

    private void SetBool(string paramName, bool value)
    {
        if (_animator.GetBool(paramName) == value)
        {
            return;
        }

        Debug.Log("[AnimatorSync] SetBool: " + paramName + " = " + value);
        _animator.SetBool(paramName, value);
    }

    public bool GetBool(string paramName)
    {
        return _animator.GetBool(paramName);
    }

    private void ClearGroundActionFlags()
    {
        SetBool(Constant.AnimatorIsIdling, false);
        SetBool(Constant.AnimatorIsWalking, false);
        SetBool(Constant.AnimatorIsSitting, false);
    }
}
