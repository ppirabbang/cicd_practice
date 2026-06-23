using TurnBasedStrategyFramework.Common.Utilities;
using TurnBasedStrategyFramework.Unity.Cells;
using TurnBasedStrategyFramework.Unity.Controllers;
using UnityEngine;

public class JustMove : MonoBehaviour
{
    [SerializeField] private ProceduralHexMapGeneratorV4 _generator;
    [SerializeField] private Vector2Int _originCoords = new Vector2Int(0, 0);

    private void Start()
    {
        var gridController = FindFirstObjectByType<UnityGridController>();

        if (gridController == null)
        {
            Debug.LogError("[JustMove] UnityGridController를 찾을 수 없습니다.");
            return;
        }

        gridController.GameInitialized += () => OnGameInitialized(gridController);
    }

    private void OnGameInitialized(UnityGridController gridController)
    {
        var cellManager = gridController.CellManager as RegularCellManager;

        if (cellManager == null)
        {
            Debug.LogError("[JustMove] CellManager를 RegularCellManager로 캐스팅 실패.");
            return;
        }

        var originCell = cellManager.GetCellAt(
            new Vector2IntImpl(_originCoords.x, _originCoords.y)
        );

        if (originCell == null)
        {
            Debug.LogError($"[JustMove] 좌표 {_originCoords}에 Cell이 없습니다. 존재하는 좌표:");
            foreach (var c in cellManager.GetCells())
                Debug.Log($"  → {c.GridCoordinates}");
            return;
        }

        _generator.AddCellsAround(originCell, cellManager);
    }
}
