using UnityEngine;

namespace TurnBasedStrategyFramework.ML
{
    /// <summary>
    /// 특정 유닛 종류에 대한 Evaluator 가중치를 저장하는 ScriptableObject.
    /// 유닛 종류별로 하나씩 생성하여 각 유닛 프리팹의 MLBehaviourTreeResource에 할당한다.
    /// 
    /// 두 가지 용도로 사용된다:
    /// 1. ML 학습 전 수동 튜닝 가중치 저장 (베이스라인 비교용)
    /// 2. ML 학습 완료 후 산출된 가중치 저장 (WeightProfileExporter가 기록)
    /// 
    /// 사용법:
    /// - Assets > Create > TBSF > ML > WeightProfile 메뉴로 생성
    /// - 유닛 프리팹의 MLBehaviourTreeResource 컴포넌트에 할당
    /// - 유닛 종류별로 하나씩 생성 (예: 보병, 궁수, 정예병 등)
    /// </summary>
    [CreateAssetMenu(fileName = "NewWeightProfile", menuName = "TBSF/ML/WeightProfile")]
    public class WeightProfile : ScriptableObject
    {
        [Header("유닛 식별")]
        [Tooltip("이 프로파일이 적용되는 유닛 종류 이름 (예: 보병, 궁수, 정예병)")]
        [SerializeField] private string _unitTypeName;

        [Header("Evaluator 가중치")]
        [SerializeField] private EvaluatorWeights _weights = EvaluatorWeights.Default;

        [Header("학습 메타데이터")]
        [Tooltip("이 가중치를 산출하는 데 사용된 학습 에피소드 수")]
        [SerializeField] private int _trainingEpisodes;

        [Tooltip("학습 중 달성한 평균 승률 (0.0 ~ 1.0)")]
        [SerializeField] private float _trainingWinRate;

        [Tooltip("이 가중치가 ML 학습으로 산출되었는지 여부. false이면 수동 튜닝 값이다.")]
        [SerializeField] private bool _isMLTrained;

        /// <summary>
        /// 이 프로파일이 적용되는 유닛 종류 이름.
        /// </summary>
        public string UnitTypeName => _unitTypeName;

        /// <summary>
        /// 이 프로파일에 저장된 Evaluator 가중치 값.
        /// </summary>
        public EvaluatorWeights Weights => _weights;

        /// <summary>
        /// 이 가중치를 산출하는 데 사용된 학습 에피소드 수.
        /// </summary>
        public int TrainingEpisodes => _trainingEpisodes;

        /// <summary>
        /// 학습 중 달성한 평균 승률.
        /// </summary>
        public float TrainingWinRate => _trainingWinRate;

        /// <summary>
        /// 이 가중치가 ML 학습으로 산출되었는지 여부.
        /// true이면 ML 학습 결과, false이면 수동 튜닝 값이다.
        /// </summary>
        public bool IsMLTrained => _isMLTrained;

        /// <summary>
        /// ML 학습이 완료된 후 가중치와 학습 메타데이터를 업데이트한다.
        /// 주로 WeightProfileExporter에서 호출된다.
        /// </summary>
        /// <param name="weights">학습으로 산출된 새 가중치 값.</param>
        /// <param name="episodes">학습에 사용된 에피소드 수.</param>
        /// <param name="winRate">학습 중 달성한 승률 (0.0 ~ 1.0).</param>
        public void SetTrainedWeights(EvaluatorWeights weights, int episodes, float winRate)
        {
            _weights = weights;
            _trainingEpisodes = episodes;
            _trainingWinRate = winRate;
            _isMLTrained = true;

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>
        /// 가중치를 기본값으로 되돌리고 학습 메타데이터를 초기화한다.
        /// 학습을 처음부터 다시 시작하거나, 수동 튜닝 값으로 비교 테스트할 때 사용한다.
        /// </summary>
        public void ResetToDefault()
        {
            _weights = EvaluatorWeights.Default;
            _trainingEpisodes = 0;
            _trainingWinRate = 0f;
            _isMLTrained = false;

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}
