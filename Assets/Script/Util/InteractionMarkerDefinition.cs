using UnityEngine;
using UnityEngine.Serialization;

[System.Serializable]
public sealed class InteractionMarkerDefinition
{
    [SerializeField] private InteractionMarkerType type;
    [SerializeField] private GameObject prefab;
    [SerializeField] [Min(0f)] private float duration = 1.2f;
    [Tooltip("Zero uses the prefab's authored scale.")]
    [SerializeField, Min(0f)] private float uniformScale;

    public InteractionMarkerType Type => type;
    public GameObject Prefab => prefab;
    public float Duration => duration;
    public float UniformScale => uniformScale;
}
