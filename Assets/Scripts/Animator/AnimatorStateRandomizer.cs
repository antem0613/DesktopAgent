using UnityEngine;

public class AnimatorStateRandomizer: StateMachineBehaviour
{
    [Header("アニメーションの総数")]
    public int stateCount;

    [Header("使用するパラメータ名")]
    public string paramName;

    public bool randomizeOnLoop;
    public bool randomizeOnExit;

    float normalizedTime;
    bool IsChangedThisCycle = false;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (animator.GetBool("IsGrabOnce"))
        {
            animator.SetBool("IsGrabOnce", false);
        }

        if (!randomizeOnExit)
        {
            int randomId = Random.Range(0, stateCount);
            animator.SetInteger(paramName, randomId);
        }
    }

    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        // 0 から stateCount - 1 までのランダムな整数をセット
        // 例: stateCountが3なら、0, 1, 2 のどれかが出る
        if (randomizeOnExit)
        {
            int randomId = Random.Range(0, stateCount);
            animator.SetInteger(paramName, randomId);
        }
    }

    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        normalizedTime = stateInfo.normalizedTime % 1f;
        if(normalizedTime > 0.9f && randomizeOnLoop && !IsChangedThisCycle)
        {
            IsChangedThisCycle = true;
            int randomId = Random.Range(0, stateCount);
            animator.SetInteger(paramName, randomId);
        }
        else if (IsChangedThisCycle && randomizeOnLoop && normalizedTime <= 0.9f)
        {
            IsChangedThisCycle = false;
        }
    }
}
