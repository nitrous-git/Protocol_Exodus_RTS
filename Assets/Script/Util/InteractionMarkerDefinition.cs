using UnityEngine;
using UnityEngine.Serialization;

[System.Serializable]
public sealed class InteractionMarkerDefinition
{
    [SerializeField] private InteractionMarkerType type;
    [SerializeField] private GameObject prefab;
    [SerializeField] [Min(0f)] private float duration = 1.2f;

    public InteractionMarkerType Type => type;
    public GameObject Prefab => prefab;
    public float Duration => duration;
}