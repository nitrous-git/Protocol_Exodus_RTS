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

    public Vector2 PointerPosition { get; private set; }

    public bool PrimaryPressed { get; private set; }
    public bool PrimaryHeld { get; private set; }
    public bool PrimaryReleased { get; private set; }

    public bool SecondaryPressed { get; private set; }
    public bool SecondaryHeld { get; private set; }
    public bool SecondaryReleased { get; private set; }

    public bool PointerOverUI { get; private set; }

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

        PointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
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
    }
}