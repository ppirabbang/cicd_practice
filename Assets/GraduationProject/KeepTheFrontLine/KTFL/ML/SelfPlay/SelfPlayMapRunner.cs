using TurnBasedStrategyFramework.Unity.Controllers;
using UnityEngine;

/// <summary>
/// Self-Play 학습 전용 맵 러너.
/// TrainingMapGenerator로 맵을 생성하고, SelfPlayUnitSpawner로 양쪽에
/// 동일한 ML 유닛을 스폰한다.
/// 
/// TrainingMapRunner와 동일한 구조이지만 SelfPlayUnitSpawner를 참조한다.
/// </summary>
public class SelfPlayMapRunner : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private TrainingMapGenerator _generator;
    [SerializeField] private SelfPlayUnitSpawner _unitSpawner;

    private void Awake()
    {
        var gridController = FindFirstObjectByType<UnityGridController>();
        if (gridController == null)
        {
            Debug.LogError("[SelfPlayMapRunner] UnityGridController를 찾을 수 없습니다.");
            return;
        }
        gridController.GameInitialized += OnGameInitialized;
    }

    private void OnDestroy()
    {
        var gridController = FindFirstObjectByType<UnityGridController>();
        if (gridController != null)
            gridController.GameInitialized -= OnGameInitialized;
    }

    /// <summary>
    /// GameInitialized 이벤트 핸들러.
    /// 씬이 매번 깨끗하게 로드되므로 정리 없이 생성만 수행한다.
    /// </summary>
    private void OnGameInitialized()
    {
        _generator.GenerateMap();

        _unitSpawner.SpawnUnits(
            _generator.GetGeneratedCells(),
            _generator.LastHeight,
            _generator.LastStartY
        );

        Debug.Log("[SelfPlayMapRunner] 대칭 맵 생성 및 양쪽 ML 유닛 스폰 완료");
    }
}
