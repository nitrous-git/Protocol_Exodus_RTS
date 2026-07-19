using UnityEngine;

/// <summary>
/// Centralized reader for keyboard input belonging to a human player.
///
/// This class reads physical keyboard state and converts it into
/// player-level input intent. It does not move the camera or issue
/// gameplay commands directly.
/// </summary>
public sealed class KeyInputHandler
{
    private readonly PlayerInputBindings bindings;

    public Vector2 CameraMovement { get; private set; }

    public KeyInputHandler(PlayerInputBindings bindings)
    {
        this.bindings = bindings;
    }

    public void TickInput()
    {
        if (bindings == null)
        {
            CameraMovement = Vector2.zero;
            return;
        }

        float horizontal = 0f;
        float vertical = 0f;

        bool moveWest = Input.GetKey(bindings.CameraWestKey) || (bindings.AllowArrowKeys && Input.GetKey(KeyCode.LeftArrow));

        bool moveEast = Input.GetKey(bindings.CameraEastKey) || (bindings.AllowArrowKeys && Input.GetKey(KeyCode.RightArrow));

        bool moveNorth = Input.GetKey(bindings.CameraNorthKey) || (bindings.AllowArrowKeys && Input.GetKey(KeyCode.UpArrow));

        bool moveSouth = Input.GetKey(bindings.CameraSouthKey) || (bindings.AllowArrowKeys && Input.GetKey(KeyCode.DownArrow));

        if (moveWest)
            horizontal -= 1f;

        if (moveEast)
            horizontal += 1f;

        if (moveSouth)
            vertical -= 1f;

        if (moveNorth)
            vertical += 1f;

        CameraMovement = Vector2.ClampMagnitude(new Vector2(horizontal, vertical), 1f);
    }

    public void Reset()
    {
        CameraMovement = Vector2.zero;
    }
}