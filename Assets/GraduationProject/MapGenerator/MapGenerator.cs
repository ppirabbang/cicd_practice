


/*
using UnityEngine;
using System.Collections.Generic;
using TbsFramework.GridGenerators; // 핵심 생성기 포함
using TurnBasedStrategyFramework.Unity.Cells;
using TurnBasedStrategyFramework.Unity.Controllers;
using TurnBasedStrategyFramework.Unity.Units;
//using UnityEditor.PackageManager;

public class RuntimeMapGenerator : MonoBehaviour
{
    [Header("Map Settings")]
    public GameObject cellPrefab;
    public int mapWidth = 10;
    public int mapHeight = 10;

    [Header("Unit Settings")]
    public Unit unitPrefab;
    public int unitsToSpawn = 2;

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
        // 1. 그리드 생성 (에셋의 알고리즘 활용)
        var generator = new RectangularHexGridGenerator();
        generator.Width = mapWidth;
        generator.Height = mapHeight;
        generator.HexagonPrefab = cellPrefab;
        generator.CellsParent = cellManager;
        generator.Is2D = false; // 3D 게임이면 false, 2D면 true

       

      

        // 에셋의 내장 함수로 타일들 생성
        GridInfo gridInfo = generator.GenerateGrid();
        Debug.Log("Map Generated!");

        // 2. 생성된 모든 셀(타일) 가져오기
        Cell[] allCells = cellManager.GetComponentsInChildren<Cell>();

        // 3. 유닛 생성 (랜덤 위치)
        SpawnUnitsRandomly(allCells);

        // 4. 중요: 프레임워크 초기화 (에셋이 맵을 인식하게 함)
        // 이 부분은 에셋 버전마다 다를 수 있으니 실행 후 에러 시 확인 필요
        gridController.CellManager = cellManager.GetComponent<RegularCellManager>();
        gridController.InitializeGame();
    }

    void SpawnUnitsRandomly(Cell[] cells)
    {
        List<Cell> cellList = new List<Cell>(cells);

        for (int i = 0; i < unitsToSpawn; i++)
        {
            if (cellList.Count == 0) break;

            // 랜덤 타일 선택
            int randomIndex = Random.Range(0, cellList.Count);
            Cell targetCell = cellList[randomIndex];

            // 유닛 생성
            Unit newUnit = Instantiate(unitPrefab, unitManager);
            newUnit.transform.position = targetCell.transform.position;

            // 유닛과 타일 연결 (중요!)
            newUnit.CurrentCell = targetCell;
            newUnit.PlayerNumber = 0; // 0번 플레이어 소속
            targetCell.IsTaken = true;

            // 이미 유닛이 들어간 타일은 리스트에서 제거 (중복 방지)
            cellList.RemoveAt(randomIndex);
        }
        Debug.Log($"{unitsToSpawn} Units Spawned!");
    }
}

*/