using System.Threading.Tasks;
using TurnBasedStrategyFramework.Unity.Units;
using UnityEngine;

namespace TurnBasedStrategyFramework.Unity.Highlighters
{
    /// <summary>
    /// A highlighter that applies a sway animation to a target.
    /// The sway effect moves the target based on the direction towards another transform, with the magnitude and duration controlled by an animation curve.
    /// </summary>
    public class SwayHighlighter : Highlighter
    {
        [SerializeField] private float _magnitude = 1f;
        [SerializeField] private float _duration = 0.5f;
        [SerializeField] private AnimationCurve _swayCurve;
        [SerializeField] private Transform _targetTransform;

        public override async Task Apply(IHighlightParams @params)
        {
            // [추가] 에피소드 전환 시 오브젝트가 파괴된 후 접근하는 것을 방지
            if (_targetTransform == null) return;

            var combatHighlightParams = (CombatHighlightParams)@params;

            
            // [추가] 전투 상대 유닛이 파괴된 경우를 방지
            if (combatHighlightParams.SecondaryUnit == null ||
                combatHighlightParams.PrimaryUnit == null) return;

            Vector3 startingPosition = _targetTransform.localPosition;
            Vector3 heading = combatHighlightParams.SecondaryUnit.transform.localPosition - combatHighlightParams.PrimaryUnit.transform.localPosition;
            Vector3 direction = heading.normalized * _magnitude;

            float elapsedTime = 0f;

            while (elapsedTime < _duration)
            {

                // [추가] 애니메이션 도중 오브젝트가 파괴되면 안전하게 중단
                if (_targetTransform == null) return;

                float t = elapsedTime / _duration;
                float swayFactor = _swayCurve.Evaluate(t);
                _targetTransform.localPosition = startingPosition + direction * swayFactor;
                elapsedTime += Time.deltaTime;
                await Awaitable.NextFrameAsync();
            }

            // [추가] 최종 위치 복원 전 null 체크
            if (_targetTransform != null)

                _targetTransform.localPosition = startingPosition;
        }
    }
}