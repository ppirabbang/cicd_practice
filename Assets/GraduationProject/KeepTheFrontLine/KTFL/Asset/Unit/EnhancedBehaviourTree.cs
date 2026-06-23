using System.Collections.Generic;
using TurnBasedStrategyFramework.Common.AI.BehaviourTrees;
using TurnBasedStrategyFramework.Common.AI.Evaluators;
using TurnBasedStrategyFramework.Common.Controllers;
using TurnBasedStrategyFramework.Common.Units;
using TurnBasedStrategyFramework.Unity.AI.BehaviourTrees;
using TurnBasedStrategyFramework.Unity.Examples.ClashOfHeroes.AI.Evaluators;
using UnityEngine;

namespace TurnBasedStrategyFramework.Unity.Examples.ClashOfHeroes.AI.BehaviourTrees
{
    /// <summary>
    /// 체력 기반 2단계 전략 분기와 높이 평가를 결합한 행동 트리.
    /// RegularBehaviourTreeResource의 체력 분기 구조에 HeightPositionEvaluator를 추가하고,
    /// 딜레이를 선택적으로 활성화할 수 있도록 개선하였다.
    /// 
    /// 트리 구조:
    /// SequenceNode (루트)
    ///   ├── SelectorNode (이동)
    ///   │     ├── [체력 높을 때] HealthThresholdNode 통과 시
    ///   │     │     └── MoveActionNode (공격적 가중치 + 높이 평가)
    ///   │     └── [체력 낮을 때] HealthThresholdNode 실패 시
    ///   │           └── MoveActionNode (방어적 가중치 + 높이 평가)
    ///   ├── (딜레이 — _useDelay가 true일 때만 삽입)
    ///   ├── AttackSequenceNode
    ///   └── (딜레이 — _useDelay가 true일 때만 삽입)
    /// 
    /// 사용법:
    /// - 유닛 프리팹의 Brain 오브젝트에 부착
    /// - ML 학습 상대(수동 가중치 AI)로 사용하거나, 실제 인게임 AI로 사용
    /// - 학습 시: _useDelay를 false로 설정하여 딜레이 없이 빠르게 진행
    /// - 인게임: _useDelay를 true로 설정하여 시각적 딜레이 적용
    /// </summary>
    public class EnhancedBehaviourTree : BehaviourTreeResource
    {
        // ============================================================
        //  딜레이 설정
        // ============================================================

        [Space]
        [Header("딜레이 설정")]
        [Tooltip("true이면 이동과 공격 사이, 턴 종료 전에 딜레이를 삽입한다. " +
                 "인게임에서는 true, ML 학습 시에는 false를 권장한다.")]
        [SerializeField] private bool _useDelay = false;

        [Tooltip("이동과 공격 사이 딜레이 (밀리초). _useDelay가 true일 때만 적용된다.")]
        [SerializeField] private int _actionDelay = 100;

        [Tooltip("턴 종료 전 딜레이 (밀리초). _useDelay가 true일 때만 적용된다.")]
        [SerializeField] private int _turnFinishDelay = 100;

        // ============================================================
        //  체력 분기 설정
        // ============================================================

        [Space]
        [Header("체력 분기 기준")]
        [Tooltip("체력이 이 비율 이상이면 공격적 가중치, 미만이면 방어적 가중치를 사용한다. " +
                 "0.5이면 체력 50% 기준으로 전략이 전환된다.")]
        [SerializeField] private float _highHealthThreshold = 0.5f;

        // ============================================================
        //  공격적 가중치 (체력이 높을 때)
        // ============================================================

        [Space]
        [Header("공격적 가중치 — 체력 높을 때")]
        [Tooltip("공격 데미지 최대화 위치 가중치. 높을수록 적에게 데미지를 줄 수 있는 위치를 선호한다.")]
        [SerializeField] private float _highHP_DamageDealtWeight = 1f;
        [Tooltip("공격 데미지 위치 평가의 거리 감쇠율.")]
        [SerializeField] private float _highHP_DamageDealtDecay = 0.5f;

        [Tooltip("피해 회피 위치 가중치. 음수일수록 피해를 적게 받는 위치를 선호한다.")]
        [SerializeField] private float _highHP_DamageReceivedWeight = -0.1f;
        [Tooltip("피해 회피 위치 평가의 거리 감쇠율.")]
        [SerializeField] private float _highHP_DamageReceivedDecay = 0.5f;

        [Tooltip("거리 가중치. 음수일수록 가까운 위치를 선호한다.")]
        [SerializeField] private float _highHP_DistanceWeight = -0.1f;
        [Tooltip("거리 평가 임계값. 이 값 이상의 거리는 평가 점수가 0이 된다.")]
        [SerializeField] private int _highHP_DistanceThreshold = 10;

        [Tooltip("높이 가중치. 양수일수록 높은 지형을 선호한다.")]
        [SerializeField] private float _highHP_HeightWeight = 0.2f;

        // ============================================================
        //  방어적 가중치 (체력이 낮을 때)
        // ============================================================

        [Space]
        [Header("방어적 가중치 — 체력 낮을 때")]
        [Tooltip("공격 데미지 최대화 위치 가중치. 공격적 가중치보다 낮게 설정하여 생존을 우선시한다.")]
        [SerializeField] private float _lowHP_DamageDealtWeight = 0.9f;
        [Tooltip("공격 데미지 위치 평가의 거리 감쇠율.")]
        [SerializeField] private float _lowHP_DamageDealtDecay = 0.5f;

        [Tooltip("피해 회피 위치 가중치. 공격적 가중치보다 크게(더 음수로) 설정하여 피해 회피를 강화한다.")]
        [SerializeField] private float _lowHP_DamageReceivedWeight = -1f;
        [Tooltip("피해 회피 위치 평가의 거리 감쇠율.")]
        [SerializeField] private float _lowHP_DamageReceivedDecay = 0.5f;

        [Tooltip("거리 가중치.")]
        [SerializeField] private float _lowHP_DistanceWeight = -0.1f;
        [Tooltip("거리 평가 임계값.")]
        [SerializeField] private int _lowHP_DistanceThreshold = 10;

        [Tooltip("높이 가중치. 체력이 낮을 때 높은 지형을 더 선호하도록 높게 설정할 수 있다.")]
        [SerializeField] private float _lowHP_HeightWeight = 0.5f;

        // ============================================================
        //  타겟 평가 가중치
        // ============================================================

        [Space]
        [Header("타겟 평가 가중치")]
        [Tooltip("적 체력 기반 타겟 선택 가중치. 양수이면 체력이 낮은 적을 우선 공격한다.")]
        [SerializeField] private float _healthTargetWeight = 1f;

        [Tooltip("데미지 기반 타겟 선택 가중치. 양수이면 더 큰 데미지를 줄 수 있는 적을 우선 공격한다.")]
        [SerializeField] private float _damageGivenTargetWeight = 1f;

        // ============================================================
        //  트리 조립
        // ============================================================

        /// <summary>
        /// 유닛과 그리드 컨트롤러를 받아 행동 트리를 조립한다.
        /// 체력 기반으로 공격적/방어적 이동 전략을 분기하고,
        /// 양쪽 모두 HeightPositionEvaluator를 포함한다.
        /// </summary>
        /// <param name="unit">이 행동 트리를 사용할 유닛.</param>
        /// <param name="gridController">게임 상태를 관리하는 그리드 컨트롤러.</param>
        public override void Initialize(IUnit unit, IGridController gridController)
        {
            // 공격적 가중치 Evaluator 리스트 (체력 높을 때)
            var highHealthEvaluators = new List<IPositionEvaluator>
            {
                new DamageDealtPositionEvaluator(_highHP_DamageDealtWeight, _highHP_DamageDealtDecay),
                new DamageReceivedPositionEvaluator(_highHP_DamageReceivedWeight, _highHP_DamageReceivedDecay),
                new DistancePositionEvaluator(_highHP_DistanceWeight, _highHP_DistanceThreshold),
                new HeightPositionEvaluator(_highHP_HeightWeight),
            };

            // 방어적 가중치 Evaluator 리스트 (체력 낮을 때)
            var lowHealthEvaluators = new List<IPositionEvaluator>
            {
                new DamageDealtPositionEvaluator(_lowHP_DamageDealtWeight, _lowHP_DamageDealtDecay),
                new DamageReceivedPositionEvaluator(_lowHP_DamageReceivedWeight, _lowHP_DamageReceivedDecay),
                new DistancePositionEvaluator(_lowHP_DistanceWeight, _lowHP_DistanceThreshold),
                new HeightPositionEvaluator(_lowHP_HeightWeight),
            };

            // 타겟 Evaluator 리스트
            var targetEvaluators = new List<ITargetEvaluator>
            {
                new HealthTargetEvaluator(_healthTargetWeight),
                new DamageDealtTargetEvaluator(_damageGivenTargetWeight),
            };

            // 트리 노드 조립
            var treeNodes = new List<ITreeNode>();

            // 1. 이동 분기 (체력 기반)
            treeNodes.Add(new SelectorNode(new List<ITreeNode>
            {
                // 체력 높을 때: 공격적 이동
                new SequenceNode(new List<ITreeNode>
                {
                    new HealthThresholdNode(unit, _highHealthThreshold),
                    new SuccederNode(
                        new MoveActionNode(unit, gridController, highHealthEvaluators)
                    ),
                }),
                // 체력 낮을 때: 방어적 이동
                new SequenceNode(new List<ITreeNode>
                {
                    new InverterNode(
                        new HealthThresholdNode(unit, _highHealthThreshold)
                    ),
                    new SuccederNode(
                        new MoveActionNode(unit, gridController, lowHealthEvaluators)
                    ),
                }),
            }));

            // 2. 이동-공격 사이 딜레이 (선택적)
            if (_useDelay && _actionDelay > 0)
            {
                treeNodes.Add(new RealtimeDelayNode(_actionDelay));
            }

            // 3. 공격
            treeNodes.Add(new SuccederNode(
                new AttackSequenceNode(unit, gridController, targetEvaluators)
            ));

            // 4. 턴 종료 전 딜레이 (선택적)
            if (_useDelay && _turnFinishDelay > 0)
            {
                treeNodes.Add(new RealtimeDelayNode(_turnFinishDelay));
            }

            BehaviourTree = new SequenceNode(treeNodes);
        }
    }
}
