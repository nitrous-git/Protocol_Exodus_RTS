using UnityEngine;

/// <summary>
/// Result of evaluating a rectangular building footprint against the grid.
/// </summary>
public readonly struct GridFootprintPlacement
{
    public GridCoord Origin { get; }

    public Vector2Int Size { get; }

    public Vector3 WorldCenter { get; }

    public bool IsValid { get; }

    public GridFootprintPlacement(
        GridCoord origin,
        Vector2Int size,
        Vector3 worldCenter,
        bool isValid)
    {
        Origin = origin;
        Size = size;
        WorldCenter = worldCenter;
        IsValid = isValid;
    }
}