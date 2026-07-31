using UnityEngine;

namespace SiroGame.StageBuilder
{
    /// <summary>
    /// 再生成時に削除してよい子オブジェクトを識別するためのマーカー。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoxStageGeneratedRoot : MonoBehaviour
    {
    }
}
