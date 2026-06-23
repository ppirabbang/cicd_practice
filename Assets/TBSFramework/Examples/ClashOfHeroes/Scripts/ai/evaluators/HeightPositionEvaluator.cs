using System.Linq;
using TurnBasedStrategyFramework.Common.AI.Evaluators;
using TurnBasedStrategyFramework.Common.Cells;
using TurnBasedStrategyFramework.Common.Controllers;
using TurnBasedStrategyFramework.Common.Units;
using TurnBasedStrategyFramework.Unity.Cells;
using TurnBasedStrategyFramework.Unity.Examples.ClashOfHeroes.Cells;

namespace TurnBasedStrategyFramework.Unity.Examples.ClashOfHeroes.AI.Evaluators
{
    /// <summary>
    /// Evaluates a grid cell뭩 desirability based on its elevation relative to the highest cell on the map.
    /// Higher cells yield higher scores, normalized by the maximum height.
    /// </summary>
    public struct HeightPositionEvaluator : IPositionEvaluator
    {
        public readonly float Weight { get; }
        private int _maxHeight;

        public HeightPositionEvaluator(float weight)
        {
            Weight = weight;
            _maxHeight = 0;
        }

        public readonly float EvaluatePosition(ICell evaluatedCell, IUnit evaluatingUnit, IGridController gridController)
        {
            return _maxHeight == 0 ? 0f : (evaluatedCell as Cell).GetComponent<IHeightComponent>().Height / (float)_maxHeight;          // 맵이 전부 평지로 생성될 때는 0으로 나눌수 없기에 그냥 0을 반환함.
        }

        public void Initialize(IUnit evaluatingUnit, IGridController gridController)
        {
            _maxHeight = gridController.CellManager.GetCells().Max(c => (c as Cell).GetComponent<IHeightComponent>().Height);
        }
    }
}