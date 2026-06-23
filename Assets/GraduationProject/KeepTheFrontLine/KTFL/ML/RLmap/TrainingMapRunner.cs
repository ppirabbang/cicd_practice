using TurnBasedStrategyFramework.Unity.Controllers;
using UnityEngine;

/// <summary>
/// 학습 전용 맵 러너.
/// 씬 재로드 방식의 에피소드 전환을 사용하므로, 정리(Cleanup) 로직이 불필요하다.
/// GameInitialized 이벤트에서 맵 생성과 유닛 스폰만 수행한다.
/// </summary>
public class TrainingMapRunner : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private TrainingMapGenerator _generator;
    [SerializeField] private TrainingUnitSpawner _unitSpawner;

    private void Awake()
    {
        var gridController = FindFirstObjectByType<UnityGridController>();
        if (gridController == null)
        {
            Debug.LogError("[TrainingMapRunner] UnityGridController를 찾을 수 없습니다.");
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
        // 맵 생성
        _generator.GenerateMap();

        // 유닛 스폰
        _unitSpawner.SpawnUnits(
            _generator.GetGeneratedCells(),
            _generator.LastHeight,
            _generator.LastStartY
        );

       // Debug.Log("[TrainingMapRunner] 대칭 맵 생성 및 유닛 스폰 완료");
    }
}