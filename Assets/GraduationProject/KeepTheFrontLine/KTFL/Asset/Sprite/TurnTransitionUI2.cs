using System.Threading;
using System.Threading.Tasks;
using TMPro;
using TurnBasedStrategyFramework.Unity.Controllers;
using UnityEngine;

namespace TurnBasedStrategyFramework.Unity.Examples.ClashOfHeroes.UI
{
    /// <summary>
    /// 턴 전환 시 "Player X turn" 메시지를 표시한다.
    /// 패널이 즉시 나타난 후 서서히 투명해지며 사라진다.
    /// 
    /// 필요 컴포넌트: _turnTransitionPanel에 CanvasGroup이 부착되어 있어야 한다.
    /// (CanvasGroup이 없으면 자동으로 추가한다.)
    /// </summary>
    public class TurnTransitionFadeUI : MonoBehaviour
    {
        [SerializeField] private UnityGridController _gridController;
        [SerializeField] private GameObject _turnTransitionPanel;
        [SerializeField] private TMP_Text _turnTransitionText;

        [Header("페이드 설정")]
        [Tooltip("패널이 나타나는 데 걸리는 시간 (초).")]
        [SerializeField] private float _fadeInDuration = 0.2f;

        [Tooltip("패널이 완전히 보이는 상태로 유지되는 시간 (초).")]
        [SerializeField] private float _displayDuration = 0.5f;

        [Tooltip("패널이 사라지는 데 걸리는 시간 (초).")]
        [SerializeField] private float _fadeDuration = 0.8f;

        private CanvasGroup _canvasGroup;
        private CancellationTokenSource _animationCts;

        private void Awake()
        {
            // CanvasGroup 확인 또는 자동 추가
            if (_turnTransitionPanel != null)
            {
                _canvasGroup = _turnTransitionPanel.GetComponent<CanvasGroup>();
                if (_canvasGroup == null)
                    _canvasGroup = _turnTransitionPanel.AddComponent<CanvasGroup>();
            }

            _gridController.TurnStarted += OnTurnStarted;
            _gridController.GameEnded += OnGameEnded;
        }

        private void OnGameEnded(Common.Controllers.GameResolvers.GameResult result)
        {
            _gridController.TurnStarted -= OnTurnStarted;
            _gridController.GameEnded -= OnGameEnded;

            // 진행 중인 애니메이션 취소
            _animationCts?.Cancel();

            // 패널 숨김
            if (_turnTransitionPanel != null)
                _turnTransitionPanel.SetActive(false);
        }

        private async void OnTurnStarted(Common.Controllers.TurnTransitionParams obj)
        {
            // 이전 애니메이션 취소
            _animationCts?.Cancel();
            _animationCts = new CancellationTokenSource();
            var token = _animationCts.Token;

            // 텍스트 설정
            if (_turnTransitionText != null)
                _turnTransitionText.text = obj.TurnContext.CurrentPlayer.PlayerNumber == 0
                    ? "Player 1 Turn"
                    : "Player 2 Turn";

            // 패널 즉시 표시 (투명 상태에서 시작)
            if (_turnTransitionPanel != null)
            {
                if (_canvasGroup != null)
                    _canvasGroup.alpha = 0f;
                _turnTransitionPanel.SetActive(true);
            }

            try
            {
                // 서서히 나타남
                await Fade(0f, 1f, _fadeInDuration, token);

                // 표시 유지
                await WaitForSeconds(_displayDuration, token);

                // 서서히 투명해지며 사라짐
                await Fade(1f, 0f, _fadeDuration, token);

                // 완전히 사라진 후 비활성화
                if (_turnTransitionPanel != null && !token.IsCancellationRequested)
                    _turnTransitionPanel.SetActive(false);
            }
            catch (System.OperationCanceledException) { }
            catch (MissingReferenceException) { }
        }

        /// <summary>
        /// CanvasGroup의 alpha를 fromAlpha에서 toAlpha로 duration에 걸쳐 변경한다.
        /// </summary>
        private async Task Fade(float fromAlpha, float toAlpha, float duration, CancellationToken token)
        {
            if (_canvasGroup == null || duration <= 0f)
            {
                if (_canvasGroup != null) _canvasGroup.alpha = toAlpha;
                return;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                token.ThrowIfCancellationRequested();
                if (this == null || _canvasGroup == null) return;

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                _canvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, t);

                await Awaitable.NextFrameAsync();
            }

            if (_canvasGroup != null)
                _canvasGroup.alpha = toAlpha;
        }

        /// <summary>
        /// 지정된 시간만큼 대기한다 (TimeScale 영향 받음).
        /// </summary>
        private async Task WaitForSeconds(float seconds, CancellationToken token)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                token.ThrowIfCancellationRequested();
                if (this == null) return;

                await Awaitable.NextFrameAsync();
                elapsed += Time.deltaTime;
            }
        }

        private void OnDestroy()
        {
            _animationCts?.Cancel();
            _animationCts?.Dispose();
        }
    }
}