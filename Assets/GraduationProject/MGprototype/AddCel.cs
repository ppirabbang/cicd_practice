using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using TurnBasedStrategyFramework.Unity.Cells;

public class ProceduralMapGenerator : MonoBehaviour
{
    [Header("References")]
    public GameObject cellManagerGameObject;
    public Cell tilePrefab;             // 추가할 Cell 프리팹

    [Header("Settings")]
    public bool is2D = false;

    // 기존 맵의 특정 Cell 기준으로 3x3 영역을 추가
    public void Add3x3CellsAt(Cell originCell)
    {
        if (cellManagerGameObject == null)
            cellManagerGameObject = GameObject.Find("CellManager");

        if (tilePrefab == null || originCell == null)
        {
            Debug.LogError("[ProceduralMap] tilePrefab 또는 originCell이 null입니다.");
            return;
        }

        // 기존 Cell 전체 수집 (중복 방지용)
        var existingCells = cellManagerGameObject
            .GetComponentsInChildren<Cell>()
            .ToList();

        // Cell 하나의 크기 파악
        Vector3 cellSize = GetCellSize(originCell);

        // 3x3 오프셋 생성 (-1, 0, 1) x (-1, 0, 1)
        var offsets = new List<Vector2Int>();
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                offsets.Add(new Vector2Int(x, y));

        var createdCells = new List<Cell>();

        foreach (var offset in offsets)
        {
            // 추가할 월드 좌표 계산
            Vector3 targetPos = is2D
                ? originCell.transform.position + new Vector3(offset.x * cellSize.x, offset.y * cellSize.y, 0f)
                : originCell.transform.position + new Vector3(offset.x * cellSize.x, 0f, offset.y * cellSize.z);

            // 이미 해당 위치에 Cell이 있으면 스킵
            bool alreadyExists = existingCells.Any(c =>
                Vector3.Distance(c.transform.position, targetPos) < 0.1f);

            if (alreadyExists)
                continue;

            // 새 Cell 생성
            Cell newCell = CreateCell(targetPos, originCell.transform.parent);
            if (newCell != null)
                createdCells.Add(newCell);
        }

        Debug.Log($"[ProceduralMap] {createdCells.Count}개의 Cell이 추가되었습니다.");
    }

    private Cell CreateCell(Vector3 worldPosition, Transform parent)
    {
        GameObject go = Instantiate(tilePrefab.gameObject, parent);
        go.transform.position = worldPosition;

        Cell cell = go.GetComponent<Cell>();
        if (cell == null)
        {
            Destroy(go);
            Debug.LogError("[ProceduralMap] 프리팹에 Cell 컴포넌트가 없습니다.");
            return null;
        }

        // ⚠️ GridCoordinates를 수동 설정해야 한다면 여기서 처리
        // cell.GridCoordinates = new Vector3Int(...);

        go.name = $"{tilePrefab.name}_{worldPosition}";
        return cell;
    }

    private Vector3 GetCellSize(Cell cell)
    {
        // CellDimensions가 cell 간격을 나타낸다고 가정
        return cell.CellDimensions;
    }
}