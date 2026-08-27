using System.Collections.Generic;
using UnityEngine;

public sealed class FormationMovementGroup
{
    private const float CostEpsilon = 0.0001f;

    private readonly TerrainGrid terrainGrid;
    private readonly FormationDestinationAllocator formationAllocator;

    private readonly List<UnitBase> members = new List<UnitBase>();
    private readonly List<Vector3> slotPositions = new List<Vector3>();
    private readonly Dictionary<UnitBase, int> slotByUnit = new Dictionary<UnitBase, int>();
    private readonly Dictionary<UnitBase, int> pendingAssignments = new Dictionary<UnitBase, int>();
    private readonly List<UnitBase> activeMembers = new List<UnitBase>();
    private readonly List<UnitBase> unassignedUnits = new List<UnitBase>();
    private readonly List<int> unassignedSlots = new List<int>();

    private readonly GridCoord formationCenterCell;
    private readonly Vector3 formationCenterWorld;

    private readonly float formationMaxNavigationRadius;
    private readonly float assemblyRadius;
    private int lastEvaluationFrame = -1;

    public int MovementGroupId { get; }
    public bool FinalAssignmentDone { get; private set; }

    public int UnitCount => members.Count;


    public FormationMovementGroup(
        int movementGroupId,
        IReadOnlyList<UnitBase> units,
        Vector3 formationCenter,
        float formationMaxNavigationRadius,
        TerrainGrid terrainGrid,
        FormationDestinationAllocator formationAllocator)
    {
        MovementGroupId = movementGroupId;

        this.terrainGrid = terrainGrid;
        this.formationAllocator = formationAllocator;
        this.formationMaxNavigationRadius =
            formationMaxNavigationRadius;

        for (int i = 0; i < units.Count; i++)
        {
            UnitBase unit = units[i];

            if (unit == null)
                continue;

            members.Add(unit);
        }

        formationCenterCell =
            terrainGrid.WorldToCell(
                formationCenter);

        formationCenterWorld =
            terrainGrid.CellToWorld(
                formationCenterCell);

        BuildSlotPositions();

        assemblyRadius =
            CalculateAssemblyRadius();

        BuildInitialTopologyAssignment();
    }

    // ---------------------------------------------------------------------
    // Public
    // ---------------------------------------------------------------------

    public int GetAssignedSlotIndex(
        UnitBase unit)
    {
        if (unit == null)
            return -1;

        if (!slotByUnit.TryGetValue(
                unit,
                out int slotIndex))
        {
            return -1;
        }

        return slotIndex;
    }

    public void Tick()
    {
        if (FinalAssignmentDone)
            return;

        // Every MoveState references this same object.
        // Evaluate the group only once per frame.
        if (lastEvaluationFrame == Time.frameCount)
            return;

        lastEvaluationFrame = Time.frameCount;

        CollectActiveMembers();

        if (activeMembers.Count == 0)
        {
            FinalAssignmentDone = true;
            return;
        }

        Vector3 groupCenter =
            CalculateGroupCenter(
                activeMembers);

        Vector3 difference =
            formationCenterWorld -
            groupCenter;

        difference.y = 0f;

        if (difference.sqrMagnitude >
            assemblyRadius * assemblyRadius)
        {
            return;
        }

        PerformFinalReassignment();
    }

    // ---------------------------------------------------------------------
    // Initial assignment
    // ---------------------------------------------------------------------

    private void BuildInitialTopologyAssignment()
    {
        slotByUnit.Clear();

        if (members.Count == 0)
            return;

        Vector3 groupCenter =
            CalculateGroupCenter(
                members);

        float unitExtentX = 0f;
        float unitExtentZ = 0f;

        for (int i = 0; i < members.Count; i++)
        {
            Vector3 offset =
                members[i].Position -
                groupCenter;

            unitExtentX =
                Mathf.Max(
                    unitExtentX,
                    Mathf.Abs(offset.x));

            unitExtentZ =
                Mathf.Max(
                    unitExtentZ,
                    Mathf.Abs(offset.z));
        }

        float slotExtentX = 0f;
        float slotExtentZ = 0f;

        for (int i = 0; i < slotPositions.Count; i++)
        {
            Vector3 offset =
                slotPositions[i] -
                formationCenterWorld;

            slotExtentX =
                Mathf.Max(
                    slotExtentX,
                    Mathf.Abs(offset.x));

            slotExtentZ =
                Mathf.Max(
                    slotExtentZ,
                    Mathf.Abs(offset.z));
        }

        unitExtentX =
            Mathf.Max(unitExtentX, 0.001f);

        unitExtentZ =
            Mathf.Max(unitExtentZ, 0.001f);

        slotExtentX =
            Mathf.Max(slotExtentX, 0.001f);

        slotExtentZ =
            Mathf.Max(slotExtentZ, 0.001f);

        PrepareUnassignedLists(
            members);

        while (unassignedUnits.Count > 0 &&
               unassignedSlots.Count > 0)
        {
            UnitBase bestUnit = null;
            int bestSlot = -1;
            float bestCost =
                float.PositiveInfinity;

            for (int unitIndex = 0;
                 unitIndex < unassignedUnits.Count;
                 unitIndex++)
            {
                UnitBase unit =
                    unassignedUnits[unitIndex];

                Vector3 unitOffset =
                    unit.Position -
                    groupCenter;

                Vector2 unitTopology =
                    new Vector2(
                        unitOffset.x /
                        unitExtentX,

                        unitOffset.z /
                        unitExtentZ);

                for (int slotListIndex = 0;
                     slotListIndex < unassignedSlots.Count;
                     slotListIndex++)
                {
                    int slotIndex =
                        unassignedSlots[
                            slotListIndex];

                    Vector3 slotOffset =
                        slotPositions[slotIndex] -
                        formationCenterWorld;

                    Vector2 slotTopology =
                        new Vector2(
                            slotOffset.x /
                            slotExtentX,

                            slotOffset.z /
                            slotExtentZ);

                    float cost =
                        (unitTopology -
                         slotTopology)
                        .sqrMagnitude;

                    if (!IsBetterPair(
                            unit,
                            slotIndex,
                            cost,
                            bestUnit,
                            bestSlot,
                            bestCost))
                    {
                        continue;
                    }

                    bestUnit = unit;
                    bestSlot = slotIndex;
                    bestCost = cost;
                }
            }

            if (bestUnit == null ||
                bestSlot < 0)
            {
                break;
            }

            slotByUnit[bestUnit] =
                bestSlot;

            unassignedUnits.Remove(
                bestUnit);

            unassignedSlots.Remove(
                bestSlot);
        }
    }

    // ---------------------------------------------------------------------
    // Final assignment
    // ---------------------------------------------------------------------

    private void PerformFinalReassignment()
    {
        pendingAssignments.Clear();

        PrepareUnassignedLists(
            activeMembers);

        //
        // At this point the group is already near the target.
        //
        // We no longer care about preserving its ORIGINAL topology.
        // We care about assigning each unit to a sensible nearby
        // remaining formation slot.
        //
        while (unassignedUnits.Count > 0 &&
               unassignedSlots.Count > 0)
        {
            UnitBase bestUnit = null;
            int bestSlot = -1;
            float bestCost =
                float.PositiveInfinity;

            for (int unitIndex = 0;
                 unitIndex < unassignedUnits.Count;
                 unitIndex++)
            {
                UnitBase unit =
                    unassignedUnits[
                        unitIndex];

                for (int slotListIndex = 0;
                     slotListIndex < unassignedSlots.Count;
                     slotListIndex++)
                {
                    int slotIndex =
                        unassignedSlots[
                            slotListIndex];

                    Vector3 difference =
                        slotPositions[slotIndex] -
                        unit.Position;

                    difference.y = 0f;

                    float cost =
                        difference.sqrMagnitude;

                    if (!IsBetterPair(
                            unit,
                            slotIndex,
                            cost,
                            bestUnit,
                            bestSlot,
                            bestCost))
                    {
                        continue;
                    }

                    bestUnit = unit;
                    bestSlot = slotIndex;
                    bestCost = cost;
                }
            }

            if (bestUnit == null ||
                bestSlot < 0)
            {
                break;
            }

            pendingAssignments[
                bestUnit] =
                bestSlot;

            unassignedUnits.Remove(
                bestUnit);

            unassignedSlots.Remove(
                bestSlot);
        }

        //
        // Important:
        // release EVERY old group destination first.
        //
        // Otherwise A cannot take B's old slot while B still
        // has it reserved.
        //
        for (int i = 0;
             i < activeMembers.Count;
             i++)
        {
            activeMembers[i]
                .ReleaseFormationDestinationForReassignment(
                    MovementGroupId);
        }

        //
        // Mark the pass complete before paths start updating.
        // There will never be a second reshuffle.
        //
        FinalAssignmentDone = true;

        foreach (
            KeyValuePair<UnitBase, int> pair
            in pendingAssignments)
        {
            UnitBase unit =
                pair.Key;

            int slotIndex =
                pair.Value;

            slotByUnit[unit] =
                slotIndex;

            unit.ReassignFormationSlot(
                MovementGroupId,
                slotIndex);
        }
    }

    // ---------------------------------------------------------------------
    // Formation geometry
    // ---------------------------------------------------------------------

    private void BuildSlotPositions()
    {
        slotPositions.Clear();

        for (int slotIndex = 0; slotIndex < members.Count; slotIndex++)
        {
            GridCoord cell =
                formationAllocator
                    .GetPreferredSlot(
                        formationCenterCell,
                        slotIndex,
                        members.Count,
                        formationMaxNavigationRadius);

            slotPositions.Add(
                terrainGrid.CellToWorld(
                    cell));
        }
    }

    private float CalculateAssemblyRadius()
    {
        float formationRadius = 0f;

        for (int i = 0;
             i < slotPositions.Count;
             i++)
        {
            Vector3 difference =
                slotPositions[i] -
                formationCenterWorld;

            difference.y = 0f;

            formationRadius =
                Mathf.Max(
                    formationRadius,
                    difference.magnitude);
        }

        //
        // We want the final pass before units are deeply packed
        // into the destination formation.
        //
        float assemblyBuffer =
            Mathf.Max(
                terrainGrid.CellSize * 4f,
                formationMaxNavigationRadius * 4f);

        return formationRadius +
               assemblyBuffer;
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private void CollectActiveMembers()
    {
        activeMembers.Clear();

        for (int i = 0;
             i < members.Count;
             i++)
        {
            UnitBase unit =
                members[i];

            if (unit == null ||
                !unit.IsAlive)
            {
                continue;
            }

            if (unit.MovementGroupId !=
                MovementGroupId)
            {
                continue;
            }

            activeMembers.Add(unit);
        }
    }

    private void PrepareUnassignedLists(
        IReadOnlyList<UnitBase> units)
    {
        unassignedUnits.Clear();
        unassignedSlots.Clear();

        for (int i = 0;
             i < units.Count;
             i++)
        {
            UnitBase unit =
                units[i];

            if (unit != null)
            {
                unassignedUnits.Add(unit);
            }
        }

        //
        // We deliberately keep the ORIGINAL formation geometry
        // even if some members left the command.
        //
        for (int slotIndex = 0;
             slotIndex < slotPositions.Count;
             slotIndex++)
        {
            unassignedSlots.Add(
                slotIndex);
        }
    }

    private Vector3 CalculateGroupCenter(
        IReadOnlyList<UnitBase> units)
    {
        if (units.Count == 0)
            return formationCenterWorld;

        Vector3 center =
            Vector3.zero;

        int validCount = 0;

        for (int i = 0;
             i < units.Count;
             i++)
        {
            UnitBase unit =
                units[i];

            if (unit == null)
                continue;

            center += unit.Position;
            validCount++;
        }

        if (validCount == 0)
            return formationCenterWorld;

        return center /
               validCount;
    }

    private bool IsBetterPair(
        UnitBase candidateUnit,
        int candidateSlot,
        float candidateCost,
        UnitBase currentUnit,
        int currentSlot,
        float currentCost)
    {
        if (candidateCost <
            currentCost - CostEpsilon)
        {
            return true;
        }

        if (Mathf.Abs(
                candidateCost -
                currentCost) >
            CostEpsilon)
        {
            return false;
        }

        //
        // Deterministic tie breaking.
        //
        if (currentUnit == null)
            return true;

        if (candidateUnit.UnitId <
            currentUnit.UnitId)
        {
            return true;
        }

        if (candidateUnit.UnitId >
            currentUnit.UnitId)
        {
            return false;
        }

        return candidateSlot <
               currentSlot;
    }
}