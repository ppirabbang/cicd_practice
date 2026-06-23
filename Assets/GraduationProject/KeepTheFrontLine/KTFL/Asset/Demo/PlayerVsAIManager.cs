using System.Collections.Generic;
using System.Linq;
using TurnBasedStrategyFramework.Common.Cells;
using TurnBasedStrategyFramework.Common.Controllers.GameResolvers;
using TurnBasedStrategyFramework.Common.Players;
using TurnBasedStrategyFramework.Unity.Cells;
using TurnBasedStrategyFramework.Unity.Controllers;
using TurnBasedStrategyFramework.Unity.Examples.ClashOfHeroes.Cells;
using TurnBasedStrategyFramework.Unity.Examples.ClashOfHeroes.Units;
using TurnBasedStrategyFramework.Unity.Units;
using UnityEngine;

/// <summary>
/// 씬 3 (사람 vs AI) 매니저.
/// GameSetupUI로부터 AI 종류와 유닛 구성을 받아
/// 맵 생성 + 유닛 스폰 + 게임 시작을 처리한다.
/// </summary>
public class PlayerVsAIManager : MonoBehaviour
{
    // =========================================================================
    // AI 상대 설정 (확장 가능)
    // =========================================================================

    /// <summary>
    /// 선택 가능한 AI 상대 프로필.
    /// Inspector에서 여러 AI를 등록해 두면 UI에서 선택 가능.
    /// </summary>
    [System.Serializable]
    public class AIOpponentProfile
    {
        [Tooltip("UI에 표시될 AI 이름.")]
        public string displayName;

        [Tooltip("이 AI가 사용하는 유닛 프리팹 리스트. " +
                 "인덱스가 humanUnitPrefabs 및 unitTypeNames와 1:1 대응.")]
        public List<Unit> unitPrefabs;

        [TextArea]
        [Tooltip("AI에 대한 설명 (UI에 표시).")]
        public string description;
    }

    /// <summary>
    /// 유닛 종류 정의.
    /// Inspector에서 이름과 사람 플레이어용 프리팹을 등록한다.
    /// </summary>
    [System.Serializable]
    public class UnitTypeInfo
    {
        [Tooltip("유닛 종류 이름 (UI 표시용).")]
        public string typeName;

        [Tooltip("사람 플레이어가 사용하는 유닛 프리팹.")]
        public Unit humanPrefab;
    }

    // =========================================================================
    // Inspector 설정
    // =========================================================================

    [Header("유닛 종류 정의")]
    [Tooltip("게임에 존재하는 유닛 종류 목록. 인덱스가 AI 프리팹 리스트와 대응.")]
    [SerializeField] private List<UnitTypeInfo> _unitTypes = new List<UnitTypeInfo>();

    [Header("AI 상대 목록 (확장 가능)")]
    [Tooltip("선택 가능한 AI 상대 리스트. 새 모델을 추가하려면 여기에 등록.")]
    [SerializeField] private List<AIOpponentProfile> _aiOpponents = new List<AIOpponentProfile>();

    [Header("스폰 설정")]
    [SerializeField] private int _minTotalUnits = 3;
    [SerializeField] private int _maxTotalUnits = 10;
    [SerializeField] private int _spawnDepth = 2;

    [Header("방향 설정")]
    [SerializeField] private HexDirection _humanFacingDirection = HexDirection.Dir0;
    [SerializeField] private HexDirection _aiFacingDirection = HexDirection.Dir3;

    [Header("참조")]
    [SerializeField] private TrainingMapGenerator _mapGenerator;
    [SerializeField] private UnityUnitManager _unitManager;
    [SerializeField] private ScriptableObject _waterCellType;

    // =========================================================================
    // 공개 프로퍼티 (UI에서 참조)
    // =========================================================================

    /// <summary>등록된 AI 상대 목록.</summary>
    public List<AIOpponentProfile> AIOpponents => _aiOpponents;

    /// <summary>유닛 종류 목록.</summary>
    public List<UnitTypeInfo> UnitTypes => _unitTypes;

    /// <summary>최소 총 유닛 수.</summary>
    public int MinTotalUnits => _minTotalUnits;

    /// <summary>최대 총 유닛 수.</summary>
    public int MaxTotalUnits => _maxTotalUnits;

    /// <summary>게임 결과 (종료 후 UI 표시용).</summary>
    public string GameResultText { get; private set; } = "";

    /// <summary>게임 진행 중 여부.</summary>
    public bool IsGameRunning { get; private set; }

    // =========================================================================
    // 내부 상태
    // =========================================================================

    private UnityGridController _gridController;
    private int _selectedAIIndex;
    private int[] _unitCounts; // 유닛 종류별 수량

    // =========================================================================
    // 게임 시작
    // =========================================================================

    /// <summary>
    /// UI에서 설정을 완료하고 게임을 시작할 때 호출한다.
    /// </summary>
    /// <param name="aiIndex">선택된 AI 상대 인덱스.</param>
    /// <param name="unitCounts">유닛 종류별 수량 배열.</param>
    public void StartGame(int aiIndex, int[] unitCounts)
    {
        _selectedAIIndex = aiIndex;
        _unitCounts = unitCounts;
        GameResultText = "";
        IsGameRunning = true;

        _gridController = FindFirstObjectByType<UnityGridController>();

        if (_gridController == null)
        {
            Debug.LogError("[PlayerVsAIManager] GridController를 찾을 수 없습니다.");
            return;
        }

        _gridController.GameInitialized += OnGameInitialized;
        _gridController.GameEnded += OnGameEnded;
        _gridController.InitializeAndStart();
    }

    // =========================================================================
    // 게임 초기화 (맵 + 유닛 스폰)
    // =========================================================================

    private void OnGameInitialized()
    {
        _gridController.GameInitialized -= OnGameInitialized;

        // 맵 생성
        _mapGenerator.GenerateMap();

        var cells = _mapGenerator.GetGeneratedCells().ToList();
        int minX = cells.Min(c => c.GridCoordinates.x);
        int maxX = cells.Max(c => c.GridCoordinates.x);

        // 왼쪽 스폰 구역 (사람 플레이어)
        var humanCells = cells
            .Where(c => c.GridCoordinates.x >= minX &&
                        c.GridCoordinates.x <= minX + _spawnDepth - 1 &&
                        !c.IsTaken && IsSpawnable(c, cells))
            .OrderBy(c => c.GridCoordinates.x)
            .ThenBy(c => c.GridCoordinates.y)
            .ToList();

        // 오른쪽 스폰 구역 (AI)
        var aiCells = cells
            .Where(c => c.GridCoordinates.x >= maxX - _spawnDepth + 1 &&
                        c.GridCoordinates.x <= maxX &&
                        !c.IsTaken && IsSpawnable(c, cells))
            .OrderBy(c => c.GridCoordinates.x)
            .ThenBy(c => c.GridCoordinates.y)
            .ToList();

        // 중앙 정렬
        int totalUnits = _unitCounts.Sum();
        humanCells = GetCenteredCells(humanCells, totalUnits);
        aiCells = GetCenteredCells(aiCells, totalUnits);

        // 사람 플레이어 유닛 스폰 (Player 0)
        SpawnUnitsForPlayer(humanCells, 0, _humanFacingDirection, useHumanPrefabs: true);

        // AI 유닛 스폰 (Player 1)
        SpawnUnitsForPlayer(aiCells, 1, _aiFacingDirection, useHumanPrefabs: false);

        Debug.Log($"[PlayerVsAIManager] 게임 시작 | AI: {_aiOpponents[_selectedAIIndex].displayName} | " +
                  $"유닛: {totalUnits}개");
    }

    // =========================================================================
    // 유닛 스폰
    // =========================================================================

    private void SpawnUnitsForPlayer(List<ICell> availableCells, int playerNumber,
                                      HexDirection facingDirection, bool useHumanPrefabs)
    {
        int cellIndex = 0;
        var aiProfile = _aiOpponents[_selectedAIIndex];

        for (int typeIndex = 0; typeIndex < _unitTypes.Count; typeIndex++)
        {
            int count = _unitCounts[typeIndex];

            Unit prefab;
            if (useHumanPrefabs)
                prefab = _unitTypes[typeIndex].humanPrefab;
            else
                prefab = aiProfile.unitPrefabs[typeIndex];

            if (prefab == null) continue;

            for (int i = 0; i < count; i++)
            {
                while (cellIndex < availableCells.Count && availableCells[cellIndex].IsTaken)
                    cellIndex++;

                if (cellIndex >= availableCells.Count) return;

                var cell = availableCells[cellIndex];
                SpawnUnit(prefab, cell, playerNumber, facingDirection);
                cellIndex++;
            }
        }
    }

    private void SpawnUnit(Unit prefab, ICell cell, int playerNumber, HexDirection facingDirection)
    {
        var go = Instantiate(prefab.gameObject, _unitManager.transform);
        var unit = go.GetComponent<Unit>();

        unit.PlayerNumber = playerNumber;
        unit.CurrentCell = cell;
        go.transform.position = (cell as Cell).transform.position;

        cell.IsTaken = true;
        cell.CurrentUnits.Add(unit);

        _unitManager.AddUnit(unit);

        if (unit is KTFLUnit ktflUnit)
        {
            ktflUnit.SetFacingDirection(facingDirection);
        }

        go.name = $"{prefab.name}_P{playerNumber}";
    }

    // =========================================================================
    // 게임 종료
    // =========================================================================

    private void OnGameEnded(GameResult gameResult)
    {
        _gridController.GameEnded -= OnGameEnded;
        IsGameRunning = false;

        bool hasWinners = gameResult.Winners != null && gameResult.Winners.Any();

        if (hasWinners)
        {
            int winner = gameResult.Winners.First().PlayerNumber;
            if (winner == 0)
                GameResultText = "Win! Player beat AI .";
            else
                GameResultText = "Lost. AI won...";
        }
        else
        {
            GameResultText = "Draw";
        }

        Debug.Log($"[PlayerVsAIManager] {GameResultText}");
    }

    // =========================================================================
    // 유틸리티
    // =========================================================================

    private bool IsSpawnable(ICell cell, List<ICell> allCells)
    {
        if (_waterCellType != null && cell is ITypedCell typedCell)
        {
            if (typedCell.CellType.Equals(_waterCellType))
                return false;
        }

        if (cell is Cell unityCell)
        {
            var cellHeight = unityCell.GetComponent<IHeightComponent>();
            if (cellHeight == null) return true;

            int myHeight = cellHeight.Height;
            return allCells.Any(other =>
            {
                if (other.Equals(cell)) return false;
                if (other.GetDistance(cell) != 1) return false;
                if (_waterCellType != null && other is ITypedCell otherTyped &&
                    otherTyped.CellType.Equals(_waterCellType)) return false;
                var otherCell = other as Cell;
                if (otherCell == null) return false;
                var otherHeight = otherCell.GetComponent<IHeightComponent>();
                if (otherHeight == null) return true;
                return Mathf.Abs(myHeight - otherHeight.Height) <= 1;
            });
        }

        return true;
    }

    private List<ICell> GetCenteredCells(List<ICell> cells, int unitCount)
    {
        if (cells.Count == 0 || unitCount <= 0) return new List<ICell>();
        int take = Mathf.Min(unitCount, cells.Count);
        int startIndex = Mathf.Max(0, (cells.Count - take) / 2);
        return cells.Skip(startIndex).Take(take).ToList();
    }
}
