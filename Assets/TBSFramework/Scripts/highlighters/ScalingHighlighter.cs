using System.Threading.Tasks;
using UnityEngine;

namespace TurnBasedStrategyFramework.Unity.Highlighters
{
    /// <summary>
    /// A highlighter that smoothly scales a transform.
    /// </summary>
    public class ScalingHighlighter : Highlighter
    {
        [SerializeField] private Transform _targetTransform;
        [SerializeField] private float _duration = 1f;
        [SerializeField] private AnimationCurve _scaleCurve;

        public override async Task Apply(IHighlightParams @params)
        {

            // [추가] 에피소드 전환 시 오브젝트가 파괴된 후 접근하는 것을 방지
            if (_targetTransform == null) return;

            Vector3 originalScale = Vector3.one;

            float elapsedTime = 0f;
            while (elapsedTime < _duration)
            {

                // [추가] 애니메이션 도중 오브젝트가 파괴되면 안전하게 중단
                if (_targetTransform == null) return;

                elapsedTime += Time.deltaTime;
                float t = Mathf.Clamp01(elapsedTime / _duration);
                float scaleMultiplier = _scaleCurve.Evaluate(t);
                _targetTransform.localScale = originalScale * scaleMultiplier;

                await Awaitable.NextFrameAsync();
            }

            // [추가] 최종 스케일 설정 전 null 체크
            if (_targetTransform != null)
            {
                _targetTransform.localScale = originalScale * _scaleCurve.Evaluate(1f);
            }
        }
    }
}
