using UnityEngine;

public class EmotionManager : MonoBehaviour
{
    public SpriteData spriteData;
    public Animator animator;

    // Update is called once per frame
    void FixedUpdate()
    {
        animator.SetInteger("emotion", spriteData.EmotionNumber);
    }
}
