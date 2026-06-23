using System.Threading.Tasks;
using UnityEngine;

namespace TurnBasedStrategyFramework.Unity.Highlighters
{
    /// <summary>
    /// A highlighter that smoothly moves a target transform from its current position to a new position using linear interpolation.
    /// The new position is determined by a position delta (relative offset) from the current position.
    /// </summary>
    public class LerpToPositionHighlighter : Highlighter
    {
        [SerializeField] private Vector3 _positionDelta;
        [SerializeField] private float _duration;
        [SerializeField] private Transform _transform;

        public override async Task Apply(IHighlightParams @params)
        {
            // [추가] 에피소드 전환 시 오브젝트가 파괴된 후 접근하는 것을 방지
            if (_transform == null) return;

            Vector3 startPosition = _transform.position;
            Vector3 targetPosition = startPosition + _positionDelta;

            float elapsedTime = 0f;

            while (elapsedTime < _duration)
            {
                // [추가] 애니메이션 도중 오브젝트가 파괴되면 안전하게 중단
                if (_transform == null) return;

                elapsedTime += Time.deltaTime;
                float t = Mathf.Clamp01(elapsedTime / _duration);
                _transform.position = Vector3.Lerp(startPosition, targetPosition, t);
                await Awaitable.NextFrameAsync();
            }


            // [추가] 최종 위치 설정 전 null 체크
            if (_transform != null)
            {
                _transform.position = targetPosition;
            }
        }
    }
}
