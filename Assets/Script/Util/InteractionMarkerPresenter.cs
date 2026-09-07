using System.Collections.Generic;
using UnityEngine;

public sealed class InteractionMarkerPresenter
{
    private const float GroundOffset = 0.05f;

    private readonly Dictionary<InteractionMarkerType, InteractionMarkerDefinition> definitions = new();
    private readonly Transform root;
    private GameObject activeMarker;

    public InteractionMarkerPresenter(IReadOnlyList<InteractionMarkerDefinition> markerDefinitions, Transform root)
    {
        this.root = root;

        if (markerDefinitions == null)
            return;

        for (int i = 0; i < markerDefinitions.Count; i++)
        {
            InteractionMarkerDefinition definition = markerDefinitions[i];

            if (definition == null)
                continue;

            if (definitions.ContainsKey(definition.Type))
            {
                Debug.LogWarning($"Duplicate interaction marker definition: {definition.Type}");
            }

            definitions[definition.Type] = definition;
        }
    }

    public void ShowActive(InteractionMarkerType type)
    {
        HideActive();

        InteractionMarkerDefinition definition = GetDefinition(type);

        if (definition == null)
            return;

        activeMarker = Object.Instantiate(definition.Prefab, root);
        activeMarker.SetActive(false);
    }

    public void UpdateActive(Vector3 groundPosition, Vector3 groundNormal, bool visible)
    {
        if (activeMarker == null)
            return;

        if (!visible)
        {
            activeMarker.SetActive(false);
            return;
        }

        SetMarkerTransform(activeMarker.transform, groundPosition, groundNormal);
        activeMarker.SetActive(true);
    }

    public void HideActive()
    {
        if (activeMarker == null)
            return;

        Object.Destroy(activeMarker);
        activeMarker = null;
    }

    public void PlayTemporary(InteractionMarkerType type, Vector3 groundPosition, Vector3 groundNormal)
    {
        InteractionMarkerDefinition definition = GetDefinition(type);

        if (definition == null)
            return;

        GameObject marker = Object.Instantiate(definition.Prefab, root);
        SetMarkerTransform(marker.transform, groundPosition, groundNormal);
        marker.SetActive(true);
        Object.Destroy(marker, definition.Duration);
    }

    private InteractionMarkerDefinition GetDefinition(InteractionMarkerType type)
    {
        if (!definitions.TryGetValue(type, out InteractionMarkerDefinition definition))
        {
            Debug.LogWarning($"Missing interaction marker definition: {type}");
            return null;
        }

        if (definition.Prefab == null)
        {
            Debug.LogWarning($"Interaction marker {type} has no prefab assigned.");
            return null;
        }

        return definition;
    }

    private static void SetMarkerTransform(Transform marker, Vector3 groundPosition, Vector3 groundNormal)
    {
        Vector3 markerPosition = groundPosition + groundNormal * GroundOffset;
        Quaternion markerRotation = Quaternion.FromToRotation(Vector3.up, groundNormal);
        marker.SetPositionAndRotation(markerPosition, markerRotation);
    }
}