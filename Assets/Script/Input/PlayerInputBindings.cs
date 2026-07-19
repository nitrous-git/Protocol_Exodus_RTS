using System;
using UnityEngine;

[Serializable]
public sealed class PlayerInputBindings
{
    [Header("Camera Movement")]
    [SerializeField] private KeyCode cameraNorthKey = KeyCode.W;
    [SerializeField] private KeyCode cameraSouthKey = KeyCode.S;
    [SerializeField] private KeyCode cameraWestKey = KeyCode.A;
    [SerializeField] private KeyCode cameraEastKey = KeyCode.D;
    [SerializeField] private bool allowArrowKeys = true;

    [Header("Pointer")]
    [SerializeField, Range(0, 6)] private int primaryPointerButton = 0;
    [SerializeField, Range(0, 6)] private int secondaryPointerButton = 1;
    [SerializeField] private bool ignoreWorldInputOverUI = true;

    [Header("Selection")]
    [SerializeField] private bool additiveSelectionEnabled = true;
    [SerializeField] private KeyCode addToSelectionKey = KeyCode.LeftShift;
    [SerializeField] private KeyCode alternateAddToSelectionKey = KeyCode.RightShift;

    public KeyCode CameraNorthKey => cameraNorthKey;
    public KeyCode CameraSouthKey => cameraSouthKey;
    public KeyCode CameraWestKey => cameraWestKey;
    public KeyCode CameraEastKey => cameraEastKey;

    public bool AllowArrowKeys => allowArrowKeys;

    public int PrimaryPointerButton => primaryPointerButton;
    public int SecondaryPointerButton => secondaryPointerButton;
    public bool IgnoreWorldInputOverUI => ignoreWorldInputOverUI;

    public bool AdditiveSelectionEnabled => additiveSelectionEnabled;
    public KeyCode AddToSelectionKey => addToSelectionKey;
    public KeyCode AlternateAddToSelectionKey => alternateAddToSelectionKey;
}