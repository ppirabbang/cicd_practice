using System.Collections.Generic;
using System.Linq;
using TurnBasedStrategyFramework.Common.Utilities;
using TurnBasedStrategyFramework.Unity.Cells;
using UnityEngine;

public class ProceduralHexMapGeneratorV6 : MonoBehaviour
{
    // =========================================================================
    // Inspector 설정값
    // =========================================================================

    [Header("References")]
    [SerializeField] private Grid _grid;
    [SerializeField] private RegularCellManager _cellManager;

    [Header("Map Settings")]
    [SerializeField] private int _defaultWidth = 20;
    [SerializeField] private int _defaultHeight = 15;
    [SerializeField] private Vector2Int _startCoords = Vector2Int.zero;

    [Tooltip("0이면 매번 랜덤 시드 사용")]
    [SerializeField] private int _seed = 0;

    [Header("Terrain Prefabs - Plains (index 0~3 = height 0~3)")]
    [SerializeField] private Hexagon[] _plainsPrefabs = new Hexagon[4];

    [Header("Terrain Prefabs - Forest (index 0~3 = height 0~3)")]
    [SerializeField] private Hexagon[] _forestPrefabs = new Hexagon[4];

    [Header("Terrain Prefabs - Water (height 0 고정)")]
    [SerializeField] private Hexagon _waterPrefab;

    [Header("Noise Settings")]
    [Tooltip("높이맵 노이즈 스케일. 클수록 지형 변화가 잦아짐")]
    [SerializeField] private float _heightNoiseScale = 0.08f;

    [Tooltip("숲 분포 노이즈 스케일")]
    [SerializeField] private float _forestNoiseScale = 0.15f;

    [Tooltip("숲 군집 임계값. 높을수록 숲이 줄어듦 (기본 0.62 ≈ 숲 22%)")]
    [SerializeField] [Range(0f, 1f)] private float _forestThreshold = 0.62f;

    [Header("River Settings")]
    [Tooltip("생성할 강 개수")]
    [SerializeField] [Range(1, 3)] private int _riverCount = 1;

    [Tooltip("강의 구불구불함 (0=직선, 1=매우 구불구불)")]
    [SerializeField] [Range(0f, 0.6f)] private float _riverWinding = 0.35f;

    // =========================================================================
    // 내부 타입
    // =========================================================================

    private enum TerrainType { Plains, Forest, Water }

    // =========================================================================
    // 공개 API
    // =========================================================================

    /// <summary>Inspector 설정값으로 직사각형 맵을 생성합니다.</summary>
    public void GenerateMap() => GenerateMap(_defaultWidth, _defaultHeight, _startCoords);

    /// <summary>지정한 크기와 중심 좌표로 직사각형 맵을 생성합니다.</summary>
    public void GenerateMap(int width, int height, Vector2Int centerCoords)
    {
        if (!ValidateReferences()) return;

        int actualSeed = _seed == 0 ? Random.Range(1, int.MaxValue) : _seed;
        var rng = new System.Random(actualSeed);
        Debug.Log($"[ProceduralHexMapGenerator] 맵 생성 시작 - 크기: {width}x{height}, Seed: {actualSeed}");

        int startX = centerCoords.x - width / 2;
        int startY = centerCoords.y - height / 2;

        // 1. 높이맵 생성 (Perlin Noise)
        float[,] heightMap = GenerateNoiseMap(width, height, _heightNoiseScale, actualSeed);

        // 2. 숲 분포 맵 생성 (별도 Perlin Noise 레이어)
        float[,] forestMap = GenerateNoiseMap(width, height, _forestNoiseScale, actualSeed + 9999);

        // 3. 강 경로 생성
        var riverCells = GenerateRivers(width, height, rng);

        // 4. 지형 타입 결정
        var terrainMap = BuildTerrainMap(width, height, heightMap, forestMap, riverCells);

        // 5. 연결성 보장 (고립 지형 제거)
        EnsureConnectivity(width, height, terrainMap);

        // 6. Cell 배치
        int createdCount = PlaceCells(width, height, startX, startY, terrainMap, heightMap);

        Debug.Log($"[ProceduralHexMapGenerator] 완료 - 생성된 Cell: {createdCount}개, Seed: {actualSeed}");
    }

    // =========================================================================
    // 지형 생성 로직
    // =========================================================================

    private float[,] GenerateNoiseMap(int width, int height, float scale, int seed)
    {
        float[,] map = new float[width, height];
        float offsetX = seed * 0.001f;
        float offsetY = seed * 0.0013f;

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                map[x, y] = Mathf.PerlinNoise(x * scale + offsetX, y * scale + offsetY);

        return map;
    }

    private HashSet<Vector2Int> GenerateRivers(int width, int height, System.Random rng)
    {
        var allRiverCells = new HashSet<Vector2Int>();

        for (int r = 0; r < _riverCount; r++)
        {
            // 맵 상단 또는 하단 가장자리에서 시작
            bool fromTop = rng.NextDouble() > 0.5;
            int startCol = rng.Next(width / 4, width * 3 / 4); // 가운데 절반에서 시작 (가장자리 강 방지)
            int startRow = fromTop ? height - 1 : 0;
            int targetRow = fromTop ? 0 : height - 1;

            var path = TraceRiverPath(startCol, startRow, targetRow, width, height, rng);
            foreach (var cell in path)
                allRiverCells.Add(cell);
        }

        return allRiverCells;
    }

    private List<Vector2Int> TraceRiverPath(int startCol, int startRow, int targetRow,
                                             int width, int height, System.Random rng)
    {
        var path = new List<Vector2Int>();
        int col = startCol;
        int row = startRow;
        bool goingDown = targetRow < startRow;

        while (true)
        {
            path.Add(new Vector2Int(col, row));
            if (row == targetRow) break;

            // 주 방향: 목표 행 쪽으로 이동
            int nextRow = row + (goingDown ? -1 : 1);
            int nextCol = col;

            // _riverWinding 확률로 옆으로 이동 (구불구불함)
            if (rng.NextDouble() < _riverWinding)
                nextCol += rng.NextDouble() > 0.5 ? 1 : -1;

            col = Mathf.Clamp(nextCol, 0, width - 1);
            row = Mathf.Clamp(nextRow, 0, height - 1);
        }

        return path;
    }

    private TerrainType[,] BuildTerrainMap(int width, int height,
                                            float[,] heightMap, float[,] forestMap,
                                            HashSet<Vector2Int> riverCells)
    {
        var terrainMap = new TerrainType[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (riverCells.Contains(new Vector2Int(x, y)))
                {
                    terrainMap[x, y] = TerrainType.Water;
                    continue;
                }

                // 숲 군집 판정 (forestMap이 임계값 이상이면 숲)
                bool isForest = forestMap[x, y] > _forestThreshold;
                terrainMap[x, y] = isForest ? TerrainType.Forest : TerrainType.Plains;
            }
        }

        return terrainMap;
    }

    // =========================================================================
    // 연결성 보장
    // =========================================================================

    private void EnsureConnectivity(int width, int height, TerrainType[,] terrainMap)
    {
        // 이동 가능(비물) Cell 중 가장 큰 연결 구역 탐색
        var visited = new bool[width, height];
        var largestGroup = new List<Vector2Int>();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (visited[x, y] || terrainMap[x, y] == TerrainType.Water) continue;

                var group = FloodFill(x, y, width, height, terrainMap, visited);
                if (group.Count > largestGroup.Count)
                    largestGroup = group;
            }
        }

        // 가장 큰 구역에 속하지 않는 고립된 육지 Cell → 평지로 유지하되 주변을 평지로 연결
        int fixedCount = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (terrainMap[x, y] != TerrainType.Water &&
                    !largestGroup.Contains(new Vector2Int(x, y)))
                {
                    // 고립된 Cell과 인접한 물 Cell을 평지로 전환하여 연결
                    foreach (var neighbor in GetLocalNeighbors(x, y, width, height))
                    {
                        if (terrainMap[neighbor.x, neighbor.y] == TerrainType.Water)
                        {
                            terrainMap[neighbor.x, neighbor.y] = TerrainType.Plains;
                            fixedCount++;
                        }
                    }
                }
            }
        }

        if (fixedCount > 0)
            Debug.Log($"[ProceduralHexMapGenerator] 연결성 보정: {fixedCount}개 물 Cell을 평지로 전환");
        else
            Debug.Log("[ProceduralHexMapGenerator] 연결성 검사 통과");
    }

    private List<Vector2Int> FloodFill(int startX, int startY, int width, int height,
                                        TerrainType[,] terrainMap, bool[,] visited)
    {
        var group = new List<Vector2Int>();
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(new Vector2Int(startX, startY));
        visited[startX, startY] = true;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            group.Add(current);

            foreach (var neighbor in GetLocalNeighbors(current.x, current.y, width, height))
            {
                if (!visited[neighbor.x, neighbor.y] &&
                    terrainMap[neighbor.x, neighbor.y] != TerrainType.Water)
                {
                    visited[neighbor.x, neighbor.y] = true;
                    queue.Enqueue(neighbor);
                }
            }
        }

        return group;
    }

    private IEnumerable<Vector2Int> GetLocalNeighbors(int x, int y, int width, int height)
    {
        // odd_r 기준 6방향
        bool isOddRow = Mathf.Abs(y) % 2 != 0;
        var offsets = isOddRow
            ? new[] { new Vector2Int(1,0),  new Vector2Int(1,1),  new Vector2Int(0,1),
                      new Vector2Int(-1,0), new Vector2Int(0,-1), new Vector2Int(1,-1) }
            : new[] { new Vector2Int(1,0),  new Vector2Int(0,1),  new Vector2Int(-1,1),
                      new Vector2Int(-1,0), new Vector2Int(-1,-1),new Vector2Int(0,-1) };

        foreach (var offset in offsets)
        {
            int nx = x + offset.x;
            int ny = y + offset.y;
            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                yield return new Vector2Int(nx, ny);
        }
    }

    // =========================================================================
    // Cell 배치
    // =========================================================================

    private int PlaceCells(int width, int height, int startX, int startY,
                            TerrainType[,] terrainMap, float[,] heightMap)
    {
        int createdCount = 0;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int col = startX + x;
                int row = startY + y;
                var coords = new Vector2IntImpl(col, row);

                if (IsCellExist(coords))
                {
                    Debug.Log($"[ProceduralHexMapGenerator] ({col},{row}) 이미 존재. 스킵.");
                    continue;
                }

                Hexagon prefab = SelectPrefab(terrainMap[x, y], heightMap[x, y]);
                if (prefab == null) continue;

                CreateAndRegisterCell(coords, prefab);
                createdCount++;
            }
        }

        return createdCount;
    }

    private Hexagon SelectPrefab(TerrainType terrain, float heightValue)
    {
        if (terrain == TerrainType.Water)
            return _waterPrefab;

        // 높이값 → Cell 높이 인덱스(0~3)
        int heightIndex;
        if (heightValue < 0.35f)      heightIndex = 0;
        else if (heightValue < 0.55f) heightIndex = 1;
        else if (heightValue < 0.75f) heightIndex = 2;
        else                          heightIndex = 3;

        return terrain == TerrainType.Forest
            ? _forestPrefabs[Mathf.Clamp(heightIndex, 0, _forestPrefabs.Length - 1)]
            : _plainsPrefabs[Mathf.Clamp(heightIndex, 0, _plainsPrefabs.Length - 1)];
    }

    private void CreateAndRegisterCell(Vector2IntImpl coords, Hexagon prefab)
    {
        Vector3 worldPos = _grid.CellToWorld(new Vector3Int(coords.x, coords.y, 0));
        worldPos.y = prefab.transform.position.y;

        var go = Instantiate(prefab.gameObject, _cellManager.transform);
        var newCell = go.GetComponent<Hexagon>();

        newCell.transform.position = worldPos;
        newCell.GridCoordinates = coords;
        newCell.GridType = prefab.GridType;
        newCell.MovementCost = 1;
        go.name = $"{prefab.name}_({coords.x},{coords.y})";

        _cellManager.AddCell(newCell);
    }

    // =========================================================================
    // 유틸리티
    // =========================================================================

    private bool IsCellExist(Vector2IntImpl coords) =>
        _cellManager.GetCells().Any(c =>
            c.GridCoordinates.x == coords.x && c.GridCoordinates.y == coords.y);

    private bool ValidateReferences()
    {
        if (_grid == null)
            { Debug.LogError("[ProceduralHexMapGenerator] Grid가 연결되지 않았습니다."); return false; }
        if (_cellManager == null)
            { Debug.LogError("[ProceduralHexMapGenerator] CellManager가 연결되지 않았습니다."); return false; }
        if (_waterPrefab == null)
            { Debug.LogError("[ProceduralHexMapGenerator] Water Prefab이 연결되지 않았습니다."); return false; }
        if (_plainsPrefabs.Any(p => p == null))
            { Debug.LogError("[ProceduralHexMapGenerator] Plains Prefab이 누락되었습니다."); return false; }
        if (_forestPrefabs.Any(p => p == null))
            { Debug.LogError("[ProceduralHexMapGenerator] Forest Prefab이 누락되었습니다."); return false; }
        return true;
    }

    // =========================================================================
    // 테스트용 - 기존 Cell 기준 주변 7칸 추가 (상호작용 검증용으로 보존)
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

            var go = Instantiate(_plainsPrefabs[0].gameObject, cellManager.transform);
            var newCell = go.GetComponent<Hexagon>();
            newCell.transform.position = worldPos;
            newCell.GridCoordinates = offsetTargetImpl;
            newCell.GridType = hexOrigin.GridType;
            go.name = $"Cell_{offsetTargetImpl}";

            cellManager.AddCell(newCell);
            existingCoords.Add(offsetTargetImpl);
        }
    }
    */
}
