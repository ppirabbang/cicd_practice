using System.Collections.Generic;
using System.Linq;
using TurnBasedStrategyFramework.Common.Controllers;
using TurnBasedStrategyFramework.Common.Units;
using TurnBasedStrategyFramework.ML;
using TurnBasedStrategyFramework.Unity.Controllers;
using TurnBasedStrategyFramework.Unity.Units;
using TurnBasedStrategyFramework.Unity.Examples.ClashOfHeroes.Units;
using UnityEngine;

namespace TurnBasedStrategyFramework.Logging
{
    /// <summary>
    /// ML Agent가 제어하는 유닛의 의사결정 과정을 콘솔에 로그로 출력하는 컴포넌트.
    /// 각 유닛이 행동할 때 사용된 Evaluator 가중치와 행동 결과(이동 목적지, 공격 대상)를 기록한다.
    /// 복수의 AI 플레이어를 지원한다.
    /// 
    /// 기존 코드 수정 없이 이벤트 구독만으로 동작한다.
    /// 
    /// 출력 예시:
    ///   ===== [AI 턴 시작] Player 1 =====
    ///   [AI 가중치] 보병(P1) | 공격위치:0.82 피해회피:-0.15 거리:-0.08 높이:0.21 체력타겟:0.95 데미지타겟:0.73
    ///   [AI 이동] 보병(P1) → (3, 5)
    ///   [AI 공격] 보병(P1) → 궁수(P0) | 데미지: 8.5
    /// 
    /// 사용법:
    /// - 씬에 빈 오브젝트를 만들고 이 컴포넌트를 부착
    /// - _gridController 필드에 씬의 UnityGridController를 할당
    /// - _aiPlayerNumbers에 로그를 출력할 AI 플레이어 번호들을 추가
    /// </summary>
    public class AIDecisionLogger : MonoBehaviour
    {
        [Header("씬 참조")]
        [Tooltip("게임 이벤트를 구독하기 위한 UnityGridController.")]
        [SerializeField] private UnityGridController _gridController;

        [Header("설정")]
        [Tooltip("로그를 출력할 AI 플레이어 번호 목록. 여러 AI 플레이어가 있으면 모두 추가한다.")]
        [SerializeField] private List<int> _aiPlayerNumbers = new List<int> { 1 };

        [Tooltip("가중치 로그를 출력할지 여부.")]
        [SerializeField] private bool _enableWeightLog = true;

        [Tooltip("이동 결과 로그를 출력할지 여부.")]
        [SerializeField] private bool _enableMoveLog = true;

        [Tooltip("공격 결과 로그를 출력할지 여부.")]
        [SerializeField] private bool _enableAttackLog = true;

        /// <summary>
        /// AI 플레이어 번호를 빠르게 검색하기 위한 HashSet.
        /// _aiPlayerNumbers 리스트에서 런타임에 생성한다.
        /// </summary>
        private HashSet<int> _aiPlayerNumberSet;

        private void OnEnable()
        {
            _aiPlayerNumberSet = new HashSet<int>(_aiPlayerNumbers);

            if (_gridController != null)
            {
                _gridController.GameInitialized += OnGameInitialized;
                _gridController.TurnStarted += OnTurnStarted;
            }
        }

        private void OnDisable()
        {
            if (_gridController != null)
            {
                _gridController.GameInitialized -= OnGameInitialized;
                _gridController.TurnStarted -= OnTurnStarted;
                UnsubscribeAllUnits();
            }
        }

        /// <summary>
        /// 주어진 플레이어 번호가 AI 플레이어인지 확인한다.
        /// </summary>
        /// <param name="playerNumber">확인할 플레이어 번호.</param>
        /// <returns>AI 플레이어이면 true.</returns>
        private bool IsAIPlayer(int playerNumber)
        {
            return _aiPlayerNumberSet.Contains(playerNumber);
        }

        /// <summary>
        /// 게임 초기화 완료 시 호출된다.
        /// 모든 유닛의 행동 이벤트를 구독한다.
        /// </summary>
        private void OnGameInitialized()
        {
            SubscribeAllUnits();
        }

        /// <summary>
        /// 턴 시작 시 호출된다.
        /// AI 플레이어의 턴이면 로그 헤더를 출력한다.
        /// </summary>
        /// <param name="turnParams">턴 전환 정보.</param>
        private void OnTurnStarted(TurnTransitionParams turnParams)
        {
            int currentPlayer = turnParams.TurnContext.CurrentPlayer.PlayerNumber;
            if (IsAIPlayer(currentPlayer))
            {
                //Debug.Log($"<color=cyan>===== [AI 턴 시작] Player {currentPlayer} =====</color>");
            }
        }

        /// <summary>
        /// 모든 유닛의 이동/공격/선택 이벤트를 구독한다.
        /// 핸들러 내에서 AI 플레이어 소속인지 필터링한다.
        /// </summary>
        private void SubscribeAllUnits()
        {
            var allUnits = _gridController.UnitManager.GetUnits();
            foreach (var unit in allUnits)
            {
                unit.UnitMoved += OnUnitMoved;
                unit.UnitAttacked += OnUnitAttacked;
                unit.UnitSelected += OnUnitSelected;
            }
        }

        /// <summary>
        /// 모든 유닛의 이벤트 구독을 해제한다.
        /// </summary>
        private void UnsubscribeAllUnits()
        {
            var allUnits = _gridController.UnitManager?.GetUnits();
            if (allUnits != null)
            {
                foreach (var unit in allUnits)
                {
                    unit.UnitMoved -= OnUnitMoved;
                    unit.UnitAttacked -= OnUnitAttacked;
                    unit.UnitSelected -= OnUnitSelected;
                }
            }
        }

        /// <summary>
        /// 유닛이 선택되었을 때 호출된다.
        /// AI 유닛이 선택되면 해당 시점의 가중치를 로그에 출력한다.
        /// AIPlayer가 유닛을 선택한 직후, BehaviourTree.Execute 직전에 호출된다.
        /// </summary>
        /// <param name="unit">선택된 유닛.</param>
        private void OnUnitSelected(IUnit unit)
        {
            // [수정] 가중치 로그는 MLBehaviourTreeResource.RefreshAndExecute에서
            // 정확한 타이밍에 출력하므로 여기서는 제거함.
            // 이 시점에서는 아직 가중치가 업데이트되지 않아 이전 값이 표시되었음.
        }

        /// <summary>
        /// 유닛이 이동했을 때 호출된다.
        /// AI 유닛의 이동이면 이동 결과를 로그에 출력한다.
        /// </summary>
        /// <param name="eventArgs">이동 이벤트 정보.</param>
        private void OnUnitMoved(UnitMovedEventArgs eventArgs)
        {
            // [추가] 씬 전환 시 파괴된 유닛 방지
            if (eventArgs.AffectedUnit == null || (eventArgs.AffectedUnit is UnityEngine.Object obj1 && obj1 == null)) return;

            if (!IsAIPlayer(eventArgs.AffectedUnit.PlayerNumber)) return;
            if (!_enableMoveLog) return;

            string unitName = GetUnitDisplayName(eventArgs.AffectedUnit);
            var targetCoords = eventArgs.TargetCell.GridCoordinates;

            Debug.Log($"<color=green>[AI 이동]</color> {unitName} → ({targetCoords.x}, {targetCoords.y})");
        }

        /// <summary>
        /// 유닛이 공격받았을 때 호출된다.
        /// 공격자가 AI 유닛이면 공격 결과를 로그에 출력한다.
        /// </summary>
        /// <param name="eventArgs">공격 이벤트 정보.</param>
        private void OnUnitAttacked(UnitAttackedEventArgs eventArgs)
        {
            // [추가] 씬 전환 시 파괴된 유닛 방지
            if (eventArgs.AttackingUnit == null || (eventArgs.AttackingUnit is UnityEngine.Object obj2 && obj2 == null)) return;

            if (!IsAIPlayer(eventArgs.AttackingUnit.PlayerNumber)) return;
            if (!_enableAttackLog) return;

            string attackerName = GetUnitDisplayName(eventArgs.AttackingUnit);
            string defenderName = GetUnitDisplayName(eventArgs.AffectedUnit);

            Debug.Log($"<color=yellow>[AI 공격]</color> {attackerName} → {defenderName} | " +
                      $"데미지: {eventArgs.DamageDealt:F1}");
        }

        /// <summary>
        /// Evaluator 가중치를 콘솔에 출력한다.
        /// 각 가중치의 이름과 값을 보기 좋은 형식으로 포맷팅한다.
        /// </summary>
        /// <param name="unit">행동하는 유닛.</param>
        /// <param name="weights">출력할 가중치.</param>
        private void LogWeights(IUnit unit, EvaluatorWeights weights)
        {
            string unitName = GetUnitDisplayName(unit);

            Debug.Log($"<color=cyan>[AI 가중치]</color> {unitName} | " +
                      $"공격위치:{weights.DamageDealtPositionWeight:F2} " +
                      $"피해회피:{weights.DamageReceivedPositionWeight:F2} " +
                      $"거리:{weights.DistancePositionWeight:F2} " +
                      $"높이:{weights.HeightPositionWeight:F2} " +
                      $"체력타겟:{weights.HealthTargetWeight:F2} " +
                      $"데미지타겟:{weights.DamageGivenTargetWeight:F2}");
        }

        /// <summary>
        /// 유닛의 표시 이름을 반환한다.
        /// IUnitDetails를 구현한 유닛이면 UnitName을, 아니면 GameObject 이름을 사용한다.
        /// </summary>
        /// <param name="unit">이름을 가져올 유닛.</param>
        /// <returns>표시용 유닛 이름.</returns>
        private string GetUnitDisplayName(IUnit unit)
        {
            if (unit is IUnitDetails details && !string.IsNullOrEmpty(details.UnitName))
            {
                return $"{details.UnitName}(P{unit.PlayerNumber})";
            }
            if (unit is Unit unityUnit)
            {
                return $"{unityUnit.gameObject.name}(P{unit.PlayerNumber})";
            }
            return $"Unit(P{unit.PlayerNumber})";
        }
    }
}