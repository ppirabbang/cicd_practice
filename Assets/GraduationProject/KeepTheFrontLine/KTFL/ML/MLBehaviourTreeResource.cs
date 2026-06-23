using System.Collections.Generic;
using System.Threading.Tasks;
using TurnBasedStrategyFramework.Common.AI.BehaviourTrees;
using TurnBasedStrategyFramework.Common.AI.Evaluators;
using TurnBasedStrategyFramework.Common.Controllers;
using TurnBasedStrategyFramework.Common.Units;
using TurnBasedStrategyFramework.Unity.AI.BehaviourTrees;
using TurnBasedStrategyFramework.Unity.Examples.ClashOfHeroes.AI.Evaluators;
using UnityEngine;

namespace TurnBasedStrategyFramework.ML
{
    /// <summary>
    /// ML 학습 가중치 또는 저장된 WeightProfile을 사용하여 행동 트리를 구성하는 BehaviourTreeResource.
    /// 기존 ClashOfHeroesBehaviourTree와 동일한 트리 구조를 사용하되,
    /// 가중치 값을 외부 소스(WeightProfile 또는 BehaviourTreeAgent)에서 가져온다.
    /// 
    /// 핵심 특징:
    /// - 유닛이 행동하기 직전(BehaviourTree.Execute 호출 시)마다 가중치를 재결정한다.
    /// - DynamicWeightTreeNode가 래퍼 역할을 하여, Execute 호출 시 최신 관찰값으로 가중치를 업데이트한다.
    /// - 이를 통해 같은 턴 내에서도 다른 유닛의 행동 결과가 반영된 가중치를 사용할 수 있다.
    /// 
    /// 사용법:
    /// - Unit 프리팹의 Brain 오브젝트에 이 컴포넌트를 부착
    /// - _weightProfile 필드에 해당 유닛 종류의 WeightProfile 에셋을 할당
    /// - 학습 시: _useMLAgent를 true로 설정하고 같은 오브젝트에 BehaviourTreeAgent를 부착
    /// - 배포 시: _useMLAgent를 true 유지 + Behavior Parameters에 학습된 .onnx 모델 할당
    /// - 수동 테스트: _useMLAgent를 false로 설정하면 WeightProfile의 고정 가중치를 사용
    /// 
    /// [확장 가이드]
    /// 새로운 Evaluator를 트리에 추가할 때:
    /// 1. BuildPositionEvaluators() 또는 BuildTargetEvaluators()에 새 Evaluator 추가
    /// 2. EvaluatorWeights 구조체에 해당 가중치 필드 추가
    /// </summary>
    public class MLBehaviourTreeResource : BehaviourTreeResource
    {
        [Header("가중치 소스")]
        [Tooltip("이 유닛 종류에 할당된 가중치 프로파일. _useMLAgent가 false일 때 이 프로파일의 가중치가 사용된다.")]
        [SerializeField] private WeightProfile _weightProfile;

        [Tooltip("true이면 BehaviourTreeAgent에서 가중치를 받고, false이면 WeightProfile에서 가져온다.")]
        [SerializeField] private bool _useMLAgent;

        /// <summary>
        /// ML Agent 참조. _useMLAgent가 true일 때 같은 오브젝트 또는 자식에서 자동으로 찾는다.
        /// </summary>
        private BehaviourTreeAgent _agent;

        /// <summary>
        /// 유닛 참조. BuildTree에서 사용하기 위해 Initialize에서 저장한다.
        /// </summary>
        private IUnit _unit;

        /// <summary>
        /// 그리드 컨트롤러 참조. BuildTree에서 사용하기 위해 Initialize에서 저장한다.
        /// </summary>
        private IGridController _gridController;

        /// <summary>
        /// 현재 사용 중인 가중치 값. 디버깅 및 로깅용으로 외부에서 읽을 수 있다.
        /// </summary>
        public EvaluatorWeights CurrentWeights { get; private set; }

        /// <summary>
        /// 유닛과 그리드 컨트롤러를 받아 행동 트리를 초기화한다.
        /// DynamicWeightTreeNode를 루트로 설정하여, Execute 호출 시마다 가중치가 재결정되도록 한다.
        /// </summary>
        /// <param name="unit">이 행동 트리를 사용할 유닛.</param>
        /// <param name="gridController">게임 상태를 관리하는 그리드 컨트롤러.</param>
        public override void Initialize(IUnit unit, IGridController gridController)
        {
            _unit = unit;
            _gridController = gridController;

            // ML Agent 모드일 경우 Agent 컴포넌트를 찾아 설정한다
            if (_useMLAgent)
            {
                _agent = GetComponentInChildren<BehaviourTreeAgent>();
                if (_agent != null)
                {
                    _agent.Setup(unit, gridController);
                }
                else
                {
                    Debug.LogWarning($"[MLBehaviourTreeResource] _useMLAgent가 true이지만 BehaviourTreeAgent를 찾을 수 없습니다. WeightProfile로 대체합니다.");
                }
            }

            // DynamicWeightTreeNode를 루트로 설정
            // 이 노드의 Execute가 호출될 때마다 최신 가중치로 내부 트리를 재구성하고 실행한다
            BehaviourTree = new DynamicWeightTreeNode(this);
        }

        /// <summary>
        /// _useMLAgent 설정에 따라 적절한 소스에서 가중치를 비동기적으로 가져온다.
        /// ML Agent 모드이면 Agent에게 현재 상황 기반 가중치를 요청하고 응답을 대기한다.
        /// WeightProfile 모드이면 저장된 고정 가중치를 즉시 반환한다.
        /// </summary>
        /// <returns>현재 상황에 맞는 Evaluator 가중치.</returns>
        public async Awaitable<EvaluatorWeights> GetWeightsAsync()
        {
            if (_useMLAgent && _agent != null)
            {
                return await _agent.RequestWeightsAsync();
            }

            if (_weightProfile != null)
            {
                return _weightProfile.Weights;
            }

            Debug.LogWarning($"[MLBehaviourTreeResource] WeightProfile이 할당되지 않았습니다. 기본 가중치를 사용합니다.");
            return EvaluatorWeights.Default;
        }

        /// <summary>
        /// 최신 가중치를 비동기적으로 가져와 행동 트리를 조립하고 실행한다.
        /// DynamicWeightTreeNode.Execute에서 호출된다.
        /// Python 프로세스의 응답을 대기한 후 트리를 실행하므로,
        /// 항상 최신 관찰값에 기반한 가중치가 적용된다.
        /// </summary>
        /// <param name="debugMode">디버그 모드 여부.</param>
        /// <returns>트리 실행 결과. 성공이면 true, 실패이면 false.</returns>
        public async Task<bool> RefreshAndExecute(bool debugMode)
        {
            CurrentWeights = await GetWeightsAsync();

            // [추가] 가중치 획득 직후 로그 출력 (정확한 타이밍)

            /*
            Debug.Log($"<color=cyan>[ML 가중치]</color> {_unit}(P{_unit.PlayerNumber}) | " +
                      $"공격위치:{CurrentWeights.DamageDealtPositionWeight:F2} " +
                      $"피해회피:{CurrentWeights.DamageReceivedPositionWeight:F2} " +
                      $"거리:{CurrentWeights.DistancePositionWeight:F2} " +
                      $"높이:{CurrentWeights.HeightPositionWeight:F2} " +
                      $"체력타겟:{CurrentWeights.HealthTargetWeight:F2} " +
                      $"데미지타겟:{CurrentWeights.DamageGivenTargetWeight:F2}");
            */

            var tree = BuildTree(_unit, _gridController, CurrentWeights);
            return await tree.Execute(debugMode);
        }

        /// <summary>
        /// 주어진 가중치를 사용하여 행동 트리를 조립한다.
        /// 트리 구조는 ClashOfHeroesBehaviourTree와 동일하다:
        /// SequenceNode (루트)
        ///   ├── SelectorNode
        ///   │     └── SequenceNode
        ///   │           └── SuccederNode
        ///   │                 └── MoveActionNode (위치 평가 가중치 사용)
        ///   └── AttackSequenceNode (타겟 평가 가중치 사용)
        /// 
        /// [확장 시] 새로운 행동 노드를 추가하려면 이 메서드의 트리 구조를 수정한다.
        /// </summary>
        /// <param name="unit">행동 트리를 실행할 유닛.</param>
        /// <param name="gridController">게임 상태를 관리하는 그리드 컨트롤러.</param>
        /// <param name="weights">트리의 각 Evaluator에 전달할 가중치 값.</param>
        /// <returns>조립 완료된 행동 트리의 루트 노드.</returns>
        private ITreeNode BuildTree(IUnit unit, IGridController gridController, EvaluatorWeights weights)
        {
            var positionEvaluators = BuildPositionEvaluators(weights);
            var targetEvaluators = BuildTargetEvaluators(weights);

            return new SequenceNode(new List<ITreeNode>
            {
                new SelectorNode(new List<ITreeNode>
                {
                    new SequenceNode(new List<ITreeNode>
                    {
                        new SuccederNode(
                            new MoveActionNode(unit, gridController, positionEvaluators)
                        ),
                    }),
                }),
                new AttackSequenceNode(unit, gridController, targetEvaluators)
            });
        }

        /// <summary>
        /// 가중치를 사용하여 위치 평가용 Evaluator 리스트를 생성한다.
        /// MoveActionNode에서 이동 목적지를 평가할 때 사용된다.
        /// 
        /// [확장 시] 새로운 위치 Evaluator를 추가하려면 이 리스트에 추가한다.
        /// </summary>
        /// <param name="weights">각 Evaluator에 전달할 가중치 값.</param>
        /// <returns>위치 평가 Evaluator 리스트.</returns>
        private List<IPositionEvaluator> BuildPositionEvaluators(EvaluatorWeights weights)
        {
            return new List<IPositionEvaluator>
            {
                new DamageDealtPositionEvaluator(
                    weights.DamageDealtPositionWeight,
                    weights.DamageDealtPositionDecay
                ),
                new DamageReceivedPositionEvaluator(
                    weights.DamageReceivedPositionWeight,
                    weights.DamageReceivedPositionDecay
                ),
                new DistancePositionEvaluator(
                    weights.DistancePositionWeight,
                    weights.DistancePositionThreshold
                ),
                new HeightPositionEvaluator(
                    weights.HeightPositionWeight
                ),
                // [확장 포인트] 새로운 위치 Evaluator 추가:
                // new NewPositionEvaluator(weights.NewPositionWeight),
            };
        }

        /// <summary>
        /// 가중치를 사용하여 타겟 평가용 Evaluator 리스트를 생성한다.
        /// AttackActionNode에서 공격 대상을 평가할 때 사용된다.
        /// 
        /// [확장 시] 새로운 타겟 Evaluator를 추가하려면 이 리스트에 추가한다.
        /// </summary>
        /// <param name="weights">각 Evaluator에 전달할 가중치 값.</param>
        /// <returns>타겟 평가 Evaluator 리스트.</returns>
        private List<ITargetEvaluator> BuildTargetEvaluators(EvaluatorWeights weights)
        {
            return new List<ITargetEvaluator>
            {
                new HealthTargetEvaluator(
                    weights.HealthTargetWeight
                ),
                new DamageDealtTargetEvaluator(
                    weights.DamageGivenTargetWeight
                ),
                // [확장 포인트] 새로운 타겟 Evaluator 추가:
                // new NewTargetEvaluator(weights.NewTargetWeight),
            };
        }
    }

    /// <summary>
    /// 행동 트리의 루트 노드를 래핑하여, Execute가 호출될 때마다 최신 가중치로 트리를 재구성하는 노드.
    /// 이 노드 덕분에 AIPlayer가 유닛의 BehaviourTree.Execute()를 호출할 때마다
    /// 해당 시점의 전장 상황이 가중치에 반영된다.
    /// 
    /// 같은 턴 내에서 유닛A가 이동한 후 유닛B가 행동할 때,
    /// 유닛B는 유닛A의 이동 결과가 반영된 관찰값을 기반으로 새 가중치를 받는다.
    /// </summary>
    public class DynamicWeightTreeNode : ITreeNode
    {
        /// <summary>
        /// 가중치 재결정과 트리 재구성을 위임할 MLBehaviourTreeResource 참조.
        /// </summary>
        private readonly MLBehaviourTreeResource _resource;

        /// <summary>
        /// DynamicWeightTreeNode를 생성한다.
        /// </summary>
        /// <param name="resource">가중치 제공 및 트리 조립을 담당하는 MLBehaviourTreeResource.</param>
        public DynamicWeightTreeNode(MLBehaviourTreeResource resource)
        {
            _resource = resource;
        }

        /// <summary>
        /// 실행 시 최신 가중치를 가져와 트리를 재구성한 뒤 실행한다.
        /// AIPlayer가 각 유닛의 BehaviourTree.Execute()를 호출할 때 이 메서드가 실행된다.
        /// 에피소드 전환 시 유닛이 파괴되면 OperationCanceledException이 발생하며,
        /// 이를 안전하게 잡아서 false를 반환한다.
        /// </summary>
        /// <param name="debugMode">디버그 모드 여부.</param>
        /// <returns>트리 실행 결과. 성공이면 true, 실패이면 false.</returns>
        public async Task<bool> Execute(bool debugMode)
        {
            try
            {
                return await _resource.RefreshAndExecute(debugMode);
            }
            catch (System.OperationCanceledException)
            {
                // [추가] 에피소드 전환 시 유닛 파괴로 인한 Awaitable 취소를 안전하게 처리
                return false;
            }
            catch (MissingReferenceException)
            {
                // [추가] 에피소드 전환 시 파괴된 오브젝트 참조를 안전하게 처리
                return false;
            }
        }
    }
}