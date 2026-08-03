using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays player economy information when nothing is selected,
/// or live information about the currently selected entities.
/// </summary>
public sealed class SelectionPanelController : MonoBehaviour
{
    [Header("View")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text infoText;

    [Header("Progress")]
    [SerializeField] private GameObject progressRoot;
    [SerializeField] private Slider progressSlider;
    [SerializeField] private TMP_Text progressText;

    private readonly StringBuilder infoBuilder = new(256);

    private Faction playerFaction;
    private GameContext gameContext;

    private string lastTitle;
    private string lastInfo;

    private bool isInitialized;

    public void Initialize(Faction playerFaction, GameContext gameContext)
    {
        this.playerFaction = playerFaction;
        this.gameContext = gameContext;

        ConfigureProgressView();
        HideProgress();

        isInitialized = true;

        Refresh();
    }

    /// <summary>
    /// Called through MatchUIController from the centralized GameLoop.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!isInitialized)
            return;

        Refresh();
    }

    private void Refresh()
    {
        IReadOnlyList<UnitBase> selectedUnits = gameContext.SelectedUnits;

        if (selectedUnits.Count > 0)
        {
            ShowUnitInfo(selectedUnits);
            return;
        }

        BuildingBase selectedBuilding = gameContext.SelectedBuilding;

        if (selectedBuilding != null)
        {
            ShowBuildingInfo(selectedBuilding);
            return;
        }

        // Later:
        // IReadOnlyList<ResourceNode> selectedResources =
        //     gameContext.SelectedResources;
        //
        // if (selectedResources.Count > 0)
        // {
        //     ShowResourceInfo(selectedResources);
        //     return;
        // }

        ShowEconomyInfo();
    }

    // ---------------------------------------------------------------------
    // Economy
    // ---------------------------------------------------------------------

    private void ShowEconomyInfo()
    {
        HideProgress();

        ResourceManager resourceManager = playerFaction.ResourceManager;
        UnitManager unitManager = playerFaction.UnitManager;

        infoBuilder.Clear();

        infoBuilder.Append("Minerals : ").AppendLine(resourceManager.Minerals.ToString());
        infoBuilder.Append("Gas : ").AppendLine(resourceManager.Gas.ToString());
        infoBuilder
            .Append("Population : ")
            .Append(unitManager.CurrentPopulation)
            .Append(" / ")
            .Append(resourceManager.MaxSupply);

        ApplyText("Economy", infoBuilder.ToString());
    }

    // ---------------------------------------------------------------------
    // Units
    // ---------------------------------------------------------------------

    private void ShowUnitInfo(IReadOnlyList<UnitBase> selectedUnits)
    {
        HideProgress();

        if (selectedUnits.Count == 1)
        {
            ShowSingleUnitInfo(selectedUnits[0]);
            return;
        }

        ShowMultipleUnitInfo(selectedUnits);
    }

    private void ShowSingleUnitInfo(UnitBase unit)
    {
        string displayName = unit.Definition.DisplayName;
        Health health = unit.Health;

        infoBuilder.Clear();

        infoBuilder.Append("Faction : ").AppendLine(unit.OwnerFaction.Name);
        infoBuilder.Append("Type : ").AppendLine(unit.Definition.unitType.ToString());
        infoBuilder
             .Append("Health : ")
             .Append(Mathf.CeilToInt(health.CurrentHealth))
             .Append(" / ")
             .Append(Mathf.CeilToInt(health.MaxHealth).ToString())
             .AppendLine(" ");

        infoBuilder.Append("State : ").AppendLine(unit.CurrentStateName);

        ApplyText(displayName, infoBuilder.ToString());
    }

    private void ShowMultipleUnitInfo(IReadOnlyList<UnitBase> selectedUnits)
    {
        if (AllSameDefinition(selectedUnits))
        {
            UnitDefinition definition = selectedUnits[0].Definition;

            infoBuilder.Clear();

            infoBuilder.Append("Count : ").Append(selectedUnits.Count);

            ApplyText(definition.DisplayName, infoBuilder.ToString());

            return;
        }

        infoBuilder.Clear();

        infoBuilder.Append("Count : ").Append(selectedUnits.Count);

        ApplyText("Multiple", infoBuilder.ToString());
    }

    // ---------------------------------------------------------------------
    // Buildings
    // ---------------------------------------------------------------------

    private void ShowBuildingInfo(BuildingBase building)
    {
        BuildingDefinition definition = building.Definition;
        Health health = building.Health;

        string displayName = definition != null ? definition.DisplayName : building.name;

        infoBuilder.Clear();

        if (building.OwnerFaction != null)
        {
            infoBuilder.Append("Faction : ").AppendLine(building.OwnerFaction.Name);
        }

        if (definition != null)
        {
            infoBuilder.Append("Type : ").AppendLine(definition.Type.ToString());
        }

        if (health != null)
        {
            infoBuilder
                .Append("Health : ")
                .Append(Mathf.CeilToInt(health.CurrentHealth))
                .Append(" / ")
                .Append(Mathf.CeilToInt(health.MaxHealth));
        }

        ApplyText(displayName, infoBuilder.ToString());
        RefreshBuildingProgress(building);
    }

    private void RefreshBuildingProgress(BuildingBase building)
    {
        if (building == null)
        {
            HideProgress();
            return;
        }

        if (building.IsUnderConstruction)
        {
            float fraction = building.ConstructionProgress();
            ShowProgress(fraction, "Completed in");
            return;
        }

        HideProgress();
    }

    // ---------------------------------------------------------------------
    // Progress Slider
    // ---------------------------------------------------------------------

    private void ConfigureProgressView()
    {
        if (progressSlider == null)
            return;

        progressSlider.minValue = 0f;
        progressSlider.maxValue = 1f;
        progressSlider.wholeNumbers = false;
        progressSlider.interactable = false;
    }

    private void ShowProgress(float fraction, string label)
    {
        fraction = Mathf.Clamp01(fraction);

        if (progressRoot != null && !progressRoot.activeSelf)
        {
            progressRoot.SetActive(true);
        }

        if (progressSlider != null)
        {
            progressSlider.value = fraction;
        }

        if (progressText != null)
        {
            int percentage = Mathf.RoundToInt(fraction * 100f);
            string displayText = $"{label}: {percentage}%";

            if (progressText.text != displayText)
            {
                progressText.text = displayText;
            }
        }
    }

    private void HideProgress()
    {
        if (progressRoot != null && progressRoot.activeSelf)
        {
            progressRoot.SetActive(false);
        }
    }

    // ---------------------------------------------------------------------
    // Helper methods
    // ---------------------------------------------------------------------

    private void ApplyText(string title, string info)
    {
        if (title != lastTitle)
        {
            lastTitle = title;

            if (titleText != null)
                titleText.text = title;
        }

        if (info != lastInfo)
        {
            lastInfo = info;

            if (infoText != null)
                infoText.text = info;
        }
    }

    private static bool AllSameDefinition(IReadOnlyList<UnitBase> selectedUnits)
    {
        if (selectedUnits.Count == 0)
            return false;

        UnitDefinition firstDefinition = selectedUnits[0].Definition;

        for (int i = 1; i < selectedUnits.Count; i++)
        {
            if (selectedUnits[i].Definition != firstDefinition)
                return false;
        }

        return true;
    }

}