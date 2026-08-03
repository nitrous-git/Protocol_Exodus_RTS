using UnityEngine;

/// <summary>
/// Controls the player's active building-placement interaction.
///
/// Responsibilities:
/// - resolve the cursor against terrain
/// - convert the cursor position to a footprint origin
/// - validate the current placement
/// - position the preview
/// - confirm construction through BuildingManager
///
/// It does not poll Unity input directly.
/// </summary>
public sealed class BuildingPlacementController
{
    private readonly TerrainGrid terrainGrid;
    private readonly BuildingManager buildingManager;
    private readonly CommandIssuer commandIssuer;

    private readonly BuildingPlacementPreview preview;

    private BuildingDefinition activeDefinition;

    private GridCoord currentFootprintOrigin;

    private bool hasCurrentPlacement;
    private bool canConstructAtCurrentPlacement;

    public bool IsActive => activeDefinition != null;

    public bool HasCurrentPlacement => hasCurrentPlacement;

    public bool CanConstructAtCurrentPlacement => canConstructAtCurrentPlacement;

    public BuildingDefinition ActiveDefinition => activeDefinition;

    public BuildingPlacementController(
    TerrainGrid terrainGrid,
    BuildingManager buildingManager,
    CommandIssuer commandIssuer,
    BuildingPlacementPreview previewPrefab,
    Transform previewRoot)
    {
        this.terrainGrid = terrainGrid;
        this.buildingManager = buildingManager;
        this.commandIssuer = commandIssuer;

        if (previewPrefab != null)
        {
            preview = Object.Instantiate(previewPrefab, previewRoot);
            preview.Hide();
        }
    }

    // ---------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------

    public void Begin(BuildingDefinition definition)
    {
        activeDefinition = definition;

        ClearCurrentPlacement();

        if (activeDefinition == null)
        {
            preview?.Hide();
            return;
        }

        preview?.Configure(activeDefinition.FootprintSize, terrainGrid.CellSize);
        preview?.Hide();
    }

    public void Cancel()
    {
        activeDefinition = null;
        ClearCurrentPlacement();
        preview?.Hide();
    }

    // ---------------------------------------------------------------------
    // Placement
    // ---------------------------------------------------------------------

    /// <summary>
    /// Updates the prospective placement from the current screen position.
    ///
    /// Returns whether the cursor currently resolves to a grid footprint.
    /// </summary>
    public bool UpdatePlacement(Vector2 screenPosition, bool pointerBlocked)
    {
        ClearCurrentPlacement();

        if (activeDefinition == null || terrainGrid == null || buildingManager == null || commandIssuer == null || pointerBlocked)
        {
            preview?.Hide();
            return false;
        }

        bool groundResolved = commandIssuer.TryResolveGroundPositionFromScreen(screenPosition);

        if (!groundResolved)
        {
            preview?.Hide();
            return false;
        }

        GridCoord centerCell = terrainGrid.WorldToCell(commandIssuer.CurrentGroundPosition);

        currentFootprintOrigin = GetFootprintOrigin(centerCell, activeDefinition.FootprintSize);

        Vector3 worldCenter = terrainGrid.GetFootprintWorldCenter(currentFootprintOrigin, activeDefinition.FootprintSize);

        canConstructAtCurrentPlacement = buildingManager.CanConstruct(activeDefinition, currentFootprintOrigin);

        hasCurrentPlacement = true;

        preview?.Show(worldCenter, canConstructAtCurrentPlacement);

        return true;
    }

    /// <summary>
    /// Attempts to construct the active building at the current footprint.
    ///
    /// Returns true only when construction succeeds.
    /// </summary>
    public bool TryConfirm()
    {
        if (activeDefinition == null)
            return false;

        if (!hasCurrentPlacement)
            return false;

        if (!canConstructAtCurrentPlacement)
            return false;

        BuildingBase building = buildingManager.Construct(activeDefinition, currentFootprintOrigin);

        return building != null;
    }

    private GridCoord GetFootprintOrigin(GridCoord centerCell, Vector2Int footprintSize)
    {
        return new GridCoord(centerCell.x - footprintSize.x / 2, centerCell.z - footprintSize.y / 2);
    }

    private void ClearCurrentPlacement()
    {
        hasCurrentPlacement = false;
        canConstructAtCurrentPlacement = false;
    }
}