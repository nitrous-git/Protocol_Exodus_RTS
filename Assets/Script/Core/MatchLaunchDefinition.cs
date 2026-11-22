using UnityEngine;

[CreateAssetMenu(menuName = "Protocol Exodus/Match Launch Definition")]
public sealed class MatchLaunchDefinition : ScriptableObject
{
    [Tooltip("Stable map identity for match selection.")]
    [SerializeField] private string mapId;
    [Tooltip("Full path of the gameplay scene in the build scene list.")]
    [SerializeField] private string scenePath;
    [SerializeField] private string displayName;

    public string MapId => mapId;
    public string ScenePath => scenePath;
    public string DisplayName => displayName;
}
