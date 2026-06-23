using TurnBasedStrategyFramework.Unity.Controllers;
using UnityEngine;

public class ProceuralMapRunnerV2 : MonoBehaviour
{
    [SerializeField] private ProceduralHexMapGeneratorV6 _generator;

    private UnityGridController _gridController; // 참조 저장 필요

    private void Awake()
    {
        Debug.Log("실행됨11234");
        Debug.Log($"[Runner] Start 시점, GameInitialized 이미 발생했는지 알 수 없음");

        _gridController = FindFirstObjectByType<UnityGridController>();
        if (_gridController == null)
        {
            Debug.LogError("[JustMove] UnityGridController를 찾을 수 없습니다.");
            return;
        }
        if (_generator == null)
        {
            Debug.LogError("_generator를 찾을 수 없습니다.");
            return;
        }

        // 중복 구독 방지: 먼저 빼고 다시 넣기
        _gridController.GameInitialized -= OnGameInitialized;
        _gridController.GameInitialized += OnGameInitialized;
    }

    private void OnDestroy()
    {
        // 반드시 구독 해제
        if (_gridController != null)
        {
            _gridController.GameInitialized -= OnGameInitialized;
        }
    }

    private void OnGameInitialized()
    {
        _generator.GenerateMap();
        Debug.Log("맵 생성");
    }
}