using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;

/// <summary>
/// 任意数(最大4)の修飾キーを要求するボタンコンポジット
/// </summary>
public sealed class ButtonWithModifiersComposite : InputBindingComposite<bool>
{
    [InputControl(layout = "Button", usage = "Modifier")]
    public int modifier1;

    [InputControl(layout = "Button", usage = "Modifier")]
    public int modifier2;

    [InputControl(layout = "Button", usage = "Modifier")]
    public int modifier3;

    [InputControl(layout = "Button", usage = "Modifier")]
    public int modifier4;

    [InputControl(layout = "Button", usage = "Button")]
    public int button;

    public override bool ReadValue(ref InputBindingCompositeContext context)
    {
        if (!IsModifierPressed(context, modifier1)) return false;
        if (!IsModifierPressed(context, modifier2)) return false;
        if (!IsModifierPressed(context, modifier3)) return false;
        if (!IsModifierPressed(context, modifier4)) return false;

        return context.ReadValueAsButton(button);
    }

    public override float EvaluateMagnitude(ref InputBindingCompositeContext context)
    {
        return ReadValue(ref context) ? 1f : 0f;
    }

    private static bool IsModifierPressed(InputBindingCompositeContext context, int partIndex)
    {
        if (partIndex == 0)
        {
            return true;
        }

        return context.ReadValueAsButton(partIndex);
    }
}
