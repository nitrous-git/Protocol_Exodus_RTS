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

    public KeyCode CameraNorthKey => cameraNorthKey;
    public KeyCode CameraSouthKey => cameraSouthKey;
    public KeyCode CameraWestKey => cameraWestKey;
    public KeyCode CameraEastKey => cameraEastKey;

    public bool AllowArrowKeys => allowArrowKeys;
}