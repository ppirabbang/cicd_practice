using TurnBasedStrategyFramework.Unity.Controllers;
using UnityEngine;

/// <summary>
/// 게임 초기화 진입점.
/// 맵 생성 → 유닛 스폰 순서를 조율합니다.
/// </summary>
public class ProceduralMapRunner : MonoBehaviour
{
    [SerializeField] private ProceduralHexMapGeneratorV7 _generator;
    [SerializeField] private UnitSpawner _unitSpawner;

    private void Awake()
    {
        var gridController = FindFirstObjectByType<UnityGridController>();

        if (gridController == null)
        {
            Debug.LogError("[ProceduralMapRunner] UnityGridController를 찾을 수 없습니다.");
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

    private void OnGameInitialized()
    {
        // 1. 맵 생성
        _generator.GenerateMap();

        // 2. 유닛 스폰 (생성된 Cell 목록과 맵 크기 정보를 넘겨줌)
        _unitSpawner.SpawnUnits(
            _generator.GetGeneratedCells(),
            _generator.LastHeight,
            _generator.LastStartY
        );

        Debug.Log("[ProceduralMapRunner] 맵 생성 및 유닛 스폰 완료");
    }
}
