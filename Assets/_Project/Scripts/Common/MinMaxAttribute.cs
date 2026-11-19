using UnityEngine;

namespace FishingVR
{
    /// <summary>
    /// Shows a Vector2 / Vector2Int range in the Inspector as "Min" and "Max" instead of X and Y.
    /// The data stays a Vector2 (x = min, y = max), so existing values are kept.
    /// </summary>
    public class MinMaxAttribute : PropertyAttribute
    {
    }
}
