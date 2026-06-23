using System.Linq;
using TurnBasedStrategyFramework.Common.Cells;
using TurnBasedStrategyFramework.Common.Utilities;
using TurnBasedStrategyFramework.Unity.Cells;
using UnityEngine;

public class ProceduralHexMapGeneratorV5 : MonoBehaviour
{
    // =========================================================================
    // Inspector 설정값
    // =========================================================================

    [Header("References")]
    [SerializeField] private Hexagon _cellPrefab;
    [SerializeField] private Grid _grid;
    [SerializeField] private RegularCellManager _cellManager;

    [Header("Map Settings")]
    [Tooltip("맵 가로 크기 (Cell 개수)")]
    [SerializeField] private int _defaultWidth = 5;

    [Tooltip("맵 세로 크기 (Cell 개수)")]
    [SerializeField] private int _defaultHeight = 5;

    [Tooltip("맵 중심 그리드 좌표")]
    [SerializeField] private Vector2Int _startCoords = Vector2Int.zero;

    // =========================================================================
    // 공개 API
    // =========================================================================

    /// <summary>
    /// Inspector에서 설정한 값으로 직사각형 맵을 생성합니다.
    /// </summary>
    public void GenerateMap()
    {
        GenerateMap(_defaultWidth, _defaultHeight, _startCoords);
    }

    /// <summary>
    /// 지정한 크기와 중심 좌표로 직사각형 맵을 생성합니다.
    /// </summary>
    /// <param name="width">가로 Cell 개수</param>
    /// <param name="height">세로 Cell 개수</param>
    /// <param name="centerCoords">맵 중심 그리드 좌표</param>
    public void GenerateMap(int width, int height, Vector2Int centerCoords)
    {
        if (!ValidateReferences()) return;

        // 중심 기준 시작 오프셋 계산
        int startX = centerCoords.x - width / 2;
        int startY = centerCoords.y - height / 2;

        int createdCount = 0;

        for (int col = startX; col < startX + width; col++)
        {
            for (int row = startY; row < startY + height; row++)
            {
                var coords = new Vector2IntImpl(col, row);

                if (IsCellExist(coords))
                {
                    Debug.Log($"[ProceduralHexMapGenerator] 좌표 ({col}, {row})에 이미 Cell이 존재합니다. 스킵합니다.");
                    continue;
                }

                CreateAndRegisterCell(coords);
                createdCount++;
            }
        }

        Debug.Log($"[ProceduralHexMapGenerator] 직사각형 맵 생성 완료 - " +
                  $"크기: {width}x{height}, 중심: {centerCoords}, 생성된 Cell: {createdCount}개");
    }

    // =========================================================================
    // 내부 구현
    // =========================================================================

    private bool IsCellExist(Vector2IntImpl coords)
    {
        return _cellManager.GetCells()
            .Any(c => c.GridCoordinates.x == coords.x &&
                      c.GridCoordinates.y == coords.y);
    }

    private void CreateAndRegisterCell(Vector2IntImpl coords)
    {
        Vector3 worldPos = _grid.CellToWorld(new Vector3Int(coords.x, coords.y, 0));
        worldPos.y = _cellPrefab.transform.position.y;

        var go = Instantiate(_cellPrefab.gameObject, _cellManager.transform);
        var newCell = go.GetComponent<Hexagon>();

        newCell.transform.position = worldPos;
        newCell.GridCoordinates = coords;
        newCell.GridType = _cellPrefab.GridType;
        newCell.MovementCost = 1;
        go.name = $"{_cellPrefab.name}_({coords.x},{coords.y})";

        _cellManager.AddCell(newCell);
    }

    private bool ValidateReferences()
    {
        if (_cellPrefab == null)
        {
            Debug.LogError("[ProceduralHexMapGenerator] CellPrefab이 연결되지 않았습니다.");
            return false;
        }
        if (_grid == null)
        {
            Debug.LogError("[ProceduralHexMapGenerator] Grid가 연결되지 않았습니다.");
            return false;
        }
        if (_cellManager == null)
        {
            Debug.LogError("[ProceduralHexMapGenerator] CellManager가 연결되지 않았습니다.");
            return false;
        }
        return true;
    }

    // =========================================================================
    // 테스트용 - 기존 Cell 기준 주변 7칸 추가 (상호작용 검증용)
    // 실제 절차적 생성에는 GenerateMap()을 사용하세요.
    // =========================================================================

    /*
    private static readonly Vector3IntImpl[] _hexRadius1 = new[]
    {
        new Vector3IntImpl( 0,  0,  0),
        new Vector3IntImpl(+1, -1,  0),
        new Vector3IntImpl(+1,  0, -1),
        new Vector3IntImpl( 0, +1, -1),
        new Vector3IntImpl(-1, +1,  0),
        new Vector3IntImpl(-1,  0, +1),
        new Vector3IntImpl( 0, -1, +1),
    };

    public void AddCellsAround(ICell originCell, RegularCellManager cellManager)
    {
        var hexOrigin = originCell as Hexagon;
        if (hexOrigin == null) return;

        var existingCoords = cellManager.GetCells()
            .Select(c => new Vector2IntImpl(c.GridCoordinates.x, c.GridCoordinates.y))
            .ToHashSet();

        var cubeOrigin = HexagonHelper.OffsetToCubeCoordinates(
            originCell.GridCoordinates, hexOrigin.GridType);

        foreach (var direction in _hexRadius1)
        {
            var cubeTarget = new Vector3IntImpl(
                cubeOrigin.x + direction.x,
                cubeOrigin.y + direction.y,
                cubeOrigin.z + direction.z
            );

            var offsetTarget     = HexagonHelper.CubeToOffsetCoordinates(cubeTarget, hexOrigin.GridType);
            var offsetTargetImpl = new Vector2IntImpl(offsetTarget.x, offsetTarget.y);

            if (existingCoords.Contains(offsetTargetImpl)) continue;

            Vector3 worldPos = _grid.CellToWorld(new Vector3Int(offsetTarget.x, offsetTarget.y, 0));
            worldPos.y = hexOrigin.transform.position.y;

            var go = Instantiate(_cellPrefab.gameObject, cellManager.transform);
            var newCell = go.GetComponent<Hexagon>();
            newCell.transform.position = worldPos;
            newCell.GridCoordinates = offsetTargetImpl;
            newCell.GridType = hexOrigin.GridType;
            go.name = $"{_cellPrefab.name}_{offsetTargetImpl}";

            cellManager.AddCell(newCell);
            existingCoords.Add(offsetTargetImpl);
        }
    }
    */
}
