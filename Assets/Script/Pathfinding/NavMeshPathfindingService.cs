using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class NavMeshPathfindingService : MonoBehaviour, IPathfindingService
{
    [SerializeField] private int areaMask = NavMesh.AllAreas;
    [SerializeField] private bool requireCompletePath = true;

    private NavMeshPath navMeshPath;

    private void Awake()
    {
        navMeshPath = new NavMeshPath();
    }

    public bool TryFindPath(Vector3 start, Vector3 end, List<Vector3> result)
    {
        result.Clear();

        bool foundPath = NavMesh.CalculatePath(start, end, areaMask, navMeshPath);
        //Debug.Log("foundPath is " + foundPath);

        if (!foundPath)
            return false;

        if (requireCompletePath && navMeshPath.status != NavMeshPathStatus.PathComplete)
            return false;

        Vector3[] corners = navMeshPath.corners;

        if (corners == null || corners.Length == 0)
            return false;

        for (int i = 0; i < corners.Length; i++)
        {
            result.Add(corners[i]);
        }

        return result.Count > 0;
    }
}
