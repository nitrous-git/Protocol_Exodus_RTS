using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Centralized reader for mouse and pointer state belonging to a
/// human-controlled player.
///
/// This class only reads raw device and UI state. It does not perform
/// selections, issue commands, or interact with the world directly.
/// </summary>
public sealed class MouseInputHandler
{
    private readonly PlayerInputBindings bindings;
    private readonly List<RaycastResult> uiHits = new();
    private PointerEventData pointerEvent;
    private EventSystem pointerEventSystem;
    private MinimapPanelController pressedMinimap;

    public Vector2 PointerPosition { get; private set; }

    public bool PrimaryPressed { get; private set; }
    public bool PrimaryHeld { get; private set; }
    public bool PrimaryReleased { get; private set; }

    public bool SecondaryPressed { get; private set; }
    public bool SecondaryHeld { get; private set; }
    public bool SecondaryReleased { get; private set; }

    public bool PointerOverUI { get; private set; }
    public bool PointerBlockedByMinimap { get; private set; }
    public bool PointerBlockedByTopBar { get; private set; }

    public MouseInputHandler(PlayerInputBindings bindings)
    {
        this.bindings = bindings;
    }

    public void TickInput()
    {
        if (bindings == null)
        {
            Reset();
            return;
        }

        PointerPosition = Input.mousePosition;

        int primaryButton = bindings.PrimaryPointerButton;
        int secondaryButton = bindings.SecondaryPointerButton;

        PrimaryPressed = Input.GetMouseButtonDown(primaryButton);
        PrimaryHeld = Input.GetMouseButton(primaryButton);
        PrimaryReleased = Input.GetMouseButtonUp(primaryButton);

        SecondaryPressed = Input.GetMouseButtonDown(secondaryButton);
        SecondaryHeld = Input.GetMouseButton(secondaryButton);
        SecondaryReleased = Input.GetMouseButtonUp(secondaryButton);

        UpdatePointerBlocking(Input.GetMouseButtonDown(0));
    }

    private void UpdatePointerBlocking(bool leftPressed)
    {
        if (!Input.GetMouseButton(0))
            pressedMinimap = null;

        PointerBlockedByMinimap = pressedMinimap != null && pressedMinimap.isActiveAndEnabled;
        PointerBlockedByTopBar = false;
        EventSystem eventSystem = EventSystem.current;
        PointerOverUI = eventSystem != null && eventSystem.IsPointerOverGameObject();
        if (eventSystem == null || !(PrimaryPressed || SecondaryPressed || leftPressed))
            return;

        // A press can reach the match tick before the EventSystem updates its cached hover target.
        if (pointerEvent == null || pointerEventSystem != eventSystem)
        {
            pointerEvent = new PointerEventData(eventSystem);
            pointerEventSystem = eventSystem;
        }

        pointerEvent.position = PointerPosition;
        eventSystem.RaycastAll(pointerEvent, uiHits);
        GameObject hit = uiHits.Count > 0 ? uiHits[0].gameObject : null;
        uiHits.Clear();
        PointerOverUI = hit != null;
        PointerBlockedByTopBar = hit != null && hit.GetComponentInParent<TopBarPanelController>() != null;

        MinimapPanelController minimap = hit != null ? hit.GetComponentInParent<MinimapPanelController>() : null;
        if (minimap == null || !minimap.isActiveAndEnabled)
            return;

        PointerBlockedByMinimap = true;
        if (leftPressed)
            pressedMinimap = minimap;
    }

    public void Reset()
    {
        PointerPosition = Vector2.zero;

        PrimaryPressed = false;
        PrimaryHeld = false;
        PrimaryReleased = false;

        SecondaryPressed = false;
        SecondaryHeld = false;
        SecondaryReleased = false;

        PointerOverUI = false;
        PointerBlockedByMinimap = false;
        PointerBlockedByTopBar = false;
        pressedMinimap = null;
    }
}
