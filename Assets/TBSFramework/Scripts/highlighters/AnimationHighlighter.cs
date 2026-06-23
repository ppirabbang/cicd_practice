using System.Threading.Tasks;
using UnityEngine;

namespace TurnBasedStrategyFramework.Unity.Highlighters
{
    /// <summary>
    /// A highlighter that triggers an animation on a given animator by setting a specified trigger parameter.
    /// Optionally, it waits for a delay after triggering the animation.
    /// </summary>
    public class AnimationHighlighter : Highlighter
    {
        [SerializeField] private Animator _animator;
        [SerializeField] private string _parameter;
        [SerializeField] private int _delay;

        public async override Task Apply(IHighlightParams @params)
        {

            // [추가] 에피소드 전환 시 오브젝트가 파괴된 후 접근하는 것을 방지
            if (_animator == null) return;

            _animator.SetTrigger(_parameter);
            await Awaitable.WaitForSecondsAsync(_delay / 1000f);
        }
    }
}