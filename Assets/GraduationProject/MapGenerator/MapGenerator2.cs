/*
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using TbsFramework.GridGenerators;
using TurnBasedStrategyFramework.Unity.Cells;
using TurnBasedStrategyFramework.Unity.Controllers;
using TurnBasedStrategyFramework.Unity.Units;

public class RuntimeMapGeneratorV2 : MonoBehaviour
{
    [Header("Map Settings")]
    public GameObject cellPrefab;
    public int mapWidth = 10;
    public int mapHeight = 10;

    [Header("Unit Spawning")]
    public List<Unit> allyPrefabs;
    public List<Unit> enemyPrefabs;
    public int allyCount = 3;
    public int enemyCount = 3;

    [Header("Required References")]
    public UnityGridController gridController;
    public Transform cellManager;
    public Transform unitManager;

    void Start()
    {
        GenerateMapAndUnits();
    }

    public void GenerateMapAndUnits()
    {
        var generator = new RectangularHexGridGenerator();
        generator.Width = mapWidth;
        generator.Height = mapHeight;
        generator.HexagonPrefab = cellPrefab;
        generator.CellsParent = cellManager;
        generator.Is2D = false;

        generator.GenerateGrid();
        Cell[] allCells = cellManager.GetComponentsInChildren<Cell>();

        // 에러 해결: OffsetCoord 대신 World Position 기반으로 구역 분리
        // 맵의 왼쪽 30% 영역은 아군, 오른쪽 30% 영역은 적군
        float minX = allCells.Min(c => c.transform.position.x);
        float maxX = allCells.Max(c => c.transform.position.x);
        float range = maxX - minX;

        var allyPossibleCells = allCells.Where(c => c.transform.position.x < minX + (range * 0.3f)).ToList();
        var enemyPossibleCells = allCells.Where(c => c.transform.position.x > minX + (range * 0.7f)).ToList();

        SpawnUnits(allyPrefabs, allyPossibleCells, 0, allyCount);
        SpawnUnits(enemyPrefabs, enemyPossibleCells, 1, enemyCount);

        gridController.CellManager = cellManager.GetComponent<RegularCellManager>();
        gridController.InitializeGame();
    }

    void SpawnUnits(List<Unit> prefabs, List<Cell> possibleCells, int playerID, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (possibleCells.Count == 0 || prefabs.Count == 0) break;

            int cellIdx = Random.Range(0, possibleCells.Count);
            int unitIdx = Random.Range(0, prefabs.Count);

            Cell targetCell = possibleCells[cellIdx];
            Unit selectedPrefab = prefabs[unitIdx];

            Unit newUnit = Instantiate(selectedPrefab, unitManager);

            // Toony RTS 에셋 대응: Y축 높이를 살짝 올려서 발이 땅에 닿게 조정
            newUnit.transform.position = targetCell.transform.position + new Vector3(0, 0.1f, 0);

            newUnit.CurrentCell = targetCell;
            newUnit.PlayerNumber = playerID;
            targetCell.IsTaken = true;

            possibleCells.RemoveAt(cellIdx);
        }
    }
}
*/