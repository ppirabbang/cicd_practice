using System.Threading.Tasks;
using UnityEngine;

namespace TurnBasedStrategyFramework.Unity.Highlighters
{
    /// <summary>
    /// A highlighter that changes the color of the target sprite renderer.
    /// </summary>
    public class SpriteRendererHighlighter : Highlighter
    {
        [SerializeField] private SpriteRenderer _sprite;
        [SerializeField] private Color _color;

        public void SetColor(Color color)
        {
            _color = color;
        }

        public override Task Apply(IHighlightParams @params)
        {

            // [추가] 에피소드 전환 시 오브젝트가 파괴된 후 접근하는 것을 방지
            if (_sprite == null) return Task.CompletedTask;

            _sprite.color = _color;
            return Task.CompletedTask;
        }
    }
}