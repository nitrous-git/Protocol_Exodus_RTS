using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class NavMeshPathfindingService : MonoBehaviour, IPathfindingService
{
    [SerializeField] private int areaMask = NavMesh.AllAreas;
    [SerializeField] private bool requireCompletePath = true;

    private NavMeshPath navMeshPath;

    [SerializeField] private float endpointSampleRadius = 2f;
    [SerializeField] private bool logPathFailures = true;

    private void Awake()
    {
        navMeshPath = new NavMeshPath();
    }

    //public bool TryFindPath(Vector3 start, Vector3 end, List<Vector3> result)
    //{
    //    result.Clear();

    //    bool foundPath = NavMesh.CalculatePath(start, end, areaMask, navMeshPath);
    //    //Debug.Log("foundPath is " + foundPath);

    //    if (!foundPath)
    //        return false;

    //    if (requireCompletePath && navMeshPath.status != NavMeshPathStatus.PathComplete)
    //        return false;

    //    Vector3[] corners = navMeshPath.corners;

    //    if (corners == null || corners.Length == 0)
    //        return false;

    //    for (int i = 0; i < corners.Length; i++)
    //    {
    //        result.Add(corners[i]);
    //    }

    //    return result.Count > 0;
    //}


    public bool TryFindPath(
    Vector3 start,
    Vector3 end,
    List<Vector3> result)
    {
        result.Clear();

        bool foundStart = NavMesh.SamplePosition(
            start,
            out NavMeshHit startHit,
            endpointSampleRadius,
            areaMask
        );

        bool foundEnd = NavMesh.SamplePosition(
            end,
            out NavMeshHit endHit,
            endpointSampleRadius,
            areaMask
        );

        Debug.DrawRay(start, Vector3.up * 2f, Color.yellow, 3f);
        Debug.DrawRay(end, Vector3.up * 2f, Color.yellow, 3f);

        if (!foundStart)
        {
            if (logPathFailures)
            {
                Debug.LogWarning(
                    $"PATH FAILED: start is not near the NavMesh. " +
                    $"Requested start: {start}"
                );
            }

            return false;
        }

        if (!foundEnd)
        {
            if (logPathFailures)
            {
                Debug.LogWarning(
                    $"PATH FAILED: destination is not near the NavMesh. " +
                    $"Requested destination: {end}"
                );
            }

            return false;
        }

        Debug.DrawRay(
            startHit.position,
            Vector3.up * 3f,
            Color.green,
            3f
        );

        Debug.DrawRay(
            endHit.position,
            Vector3.up * 3f,
            Color.cyan,
            3f
        );

        bool calculated = NavMesh.CalculatePath(
            startHit.position,
            endHit.position,
            areaMask,
            navMeshPath
        );

        if (!calculated)
        {
            if (logPathFailures)
            {
                Debug.LogWarning(
                    $"PATH FAILED: CalculatePath returned false. " +
                    $"Start: {startHit.position}, End: {endHit.position}"
                );
            }

            return false;
        }

        Vector3[] corners = navMeshPath.corners;

        if (logPathFailures)
        {
            Debug.Log(
                $"PATH RESULT: {navMeshPath.status} | " +
                $"Requested end: {end} | " +
                $"Sampled end: {endHit.position} | " +
                $"Corners: {corners.Length}"
            );
        }

        for (int i = 0; i < corners.Length - 1; i++)
        {
            Debug.DrawLine(
                corners[i] + Vector3.up * 0.15f,
                corners[i + 1] + Vector3.up * 0.15f,
                navMeshPath.status == NavMeshPathStatus.PathComplete
                    ? Color.green
                    : Color.red,
                3f
            );
        }

        if (requireCompletePath &&
            navMeshPath.status != NavMeshPathStatus.PathComplete)
        {
            if (corners.Length > 0)
            {
                Vector3 lastCorner = corners[corners.Length - 1];

                Debug.DrawRay(
                    lastCorner,
                    Vector3.up * 5f,
                    Color.magenta,
                    3f
                );

                Debug.LogWarning(
                    $"PATH PARTIAL: navigation stops at {lastCorner}. " +
                    $"Inspect the NavMesh around that point."
                );
            }

            return false;
        }

        if (corners == null || corners.Length == 0)
            return false;

        for (int i = 0; i < corners.Length; i++)
            result.Add(corners[i]);

        return true;
    }
}
