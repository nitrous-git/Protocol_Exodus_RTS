using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Displays player economy information when nothing is selected,
/// or live information about the currently selected entities.
/// </summary>
public sealed class SelectionPanelController : MonoBehaviour
{
    [Header("View")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text infoText;

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

        if (selectedUnits.Count == 0)
        {
            ShowEconomyInfo();
            return;
        }

        ShowUnitInfo(selectedUnits);
    }

    // ---------------------------------------------------------------------
    // Economy
    // ---------------------------------------------------------------------

    private void ShowEconomyInfo()
    {
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
        UnitHealth health = unit.Health;

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