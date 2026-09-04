using Unity.GraphToolkit.Editor;
using UnityEngine;

/// <summary>
/// Represents one finite, neutral resource deposit in the match.
///
/// Resource extraction removes stock from this node but does not directly
/// credit a faction. Worker units will carry and deliver extracted resources.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class ResourceNode : MonoBehaviour, ISelectable
{
    [Header("Resource")]
    [SerializeField] private ResourceType resourceType = ResourceType.Minerals;

    [SerializeField, Min(1)]
    private int initialAmount = 1500;

    [Header("Selection")]
    [SerializeField] private bool canBeSelected = true;
    [SerializeField] private Transform selectionPoint;
    [SerializeField] private GameObject selectionVisual;

    private ResourceNodeRepository repository;

    public ResourceType ResourceType => resourceType;

    public int InitialAmount => initialAmount;
    public int RemainingAmount { get; private set; }

    public int ResourceNodeId => 0;

    public GridCoord OccupiedCell { get; private set; }

    public bool IsInitialized { get; private set; }
    public bool IsDepleted => RemainingAmount <= 0;

    public bool IsSelected { get; private set; }

    public bool CanBeSelected => canBeSelected && IsInitialized && !IsDepleted;
    public Vector3 Position => transform.position;
    public Vector3 SelectionPosition => selectionPoint != null ? selectionPoint.position : transform.position;

    private void Awake()
    {
        RemainingAmount = Mathf.Max(1, initialAmount);

        if (selectionVisual != null)
            selectionVisual.SetActive(false);

    }

    /// <summary>
    /// Initializes a scene-authored resource node.
    ///
    /// Called by ResourceNodeRepository during match construction.
    /// </summary>
    internal void Initialize(ResourceNodeRepository repository, GridCoord occupiedCell)
    {
        if (IsInitialized)
            return;

        if (repository == null)
        {
            Debug.LogError($"{name} cannot initialize because its repository is missing.");
            return;
        }

        this.repository = repository;

        OccupiedCell = occupiedCell;
        RemainingAmount = Mathf.Max(1, initialAmount);

        IsInitialized = true;

        SetSelected(false);

    }

    /// <summary>
    /// Removes up to requestedAmount resources and returns the amount
    /// actually extracted.
    ///
    /// The caller is responsible for storing and eventually delivering
    /// the returned resources.
    /// </summary>
    public int Extract(int requestedAmount)
    {
        if (!IsInitialized)
            return 0;

        if (requestedAmount <= 0)
            return 0;

        if (IsDepleted)
            return 0;

        int extractedAmount = Mathf.Min(requestedAmount, RemainingAmount);

        RemainingAmount -= extractedAmount;

        if (IsDepleted)
        {
            RemainingAmount = 0;
            SetSelected(false);
        }

        return extractedAmount;
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected && CanBeSelected;

        if (selectionVisual != null)
            selectionVisual.SetActive(IsSelected);
    }

    /// <summary>
    /// Called immediately before the repository destroys this node.
    /// </summary>
    internal void NotifyRemoved()
    {
        SetSelected(false);

        IsInitialized = false;
        repository = null;
    }

    private void OnDestroy()
    {
        if (repository == null)
            return;

        ResourceNodeRepository previousRepository = repository;
        repository = null;

        previousRepository.UnregisterNode(this);
    }

    private void OnValidate()
    {
        initialAmount = Mathf.Max(1, initialAmount);
    }
}