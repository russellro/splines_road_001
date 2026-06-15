using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class TrackDataBaker : MonoBehaviour
{
    [Header("Required References")]
    [SerializeField] private SplineContainer sourceSpline;
    [SerializeField] private TrackData destinationTrackData;

    [Header("Road Surface")]
    [SerializeField] private LayerMask roadSurfaceLayer;

    [Tooltip("How far above each sample the raycast begins.")]
    [SerializeField, Min(0.1f)] private float rayStartHeight = 20f;

    [Tooltip("How far downward each raycast searches.")]
    [SerializeField, Min(0.1f)] private float rayDistance = 50f;

    [Tooltip("Small clearance above the visible road surface.")]
    [SerializeField, Min(0f)] private float surfaceClearance = 0.08f;

    [Tooltip("Stop baking if any road-surface raycast misses.")]
    [SerializeField] private bool requireSurfaceHit = true;

    [Header("Sampling")]
    [Tooltip("Distance in meters between gameplay samples.")]
    [SerializeField, Min(0.25f)] private float sampleSpacing = 1f;

    [Tooltip("Resolution used internally to convert spline progress into physical distance.")]
    [SerializeField, Min(256)] private int distanceLookupResolution = 4096;

    [Header("Height Smoothing")]
    [Tooltip("Number of nearby samples used to smooth the elevation profile.")]
    [SerializeField, Range(0, 20)] private int smoothingRadius = 5;

    [Tooltip("Number of smoothing passes applied to the elevation profile.")]
    [SerializeField, Range(0, 8)] private int smoothingPasses = 3;

    [Tooltip("0 keeps raw height. 1 applies full smoothing.")]
    [SerializeField, Range(0f, 1f)] private float smoothingStrength = 0.85f;

    [ContextMenu("Bake Track Data")]
    public void BakeTrackData()
    {
        if (sourceSpline == null)
        {
            Debug.LogError($"{name}: Assign a Source Spline.");
            return;
        }

        if (destinationTrackData == null)
        {
            Debug.LogError($"{name}: Assign a Destination Track Data asset.");
            return;
        }

        if (sourceSpline.Spline == null || sourceSpline.Spline.Count < 2)
        {
            Debug.LogError($"{name}: Source spline does not contain enough knots.");
            return;
        }

        Physics.SyncTransforms();

        bool isClosedLoop = sourceSpline.Spline.Closed;

        BuildDistanceLookup(
            out List<float> lookupDistances,
            out List<float> lookupTimes,
            out float trackLength);

        if (trackLength <= 0f || float.IsNaN(trackLength))
        {
            Debug.LogError($"{name}: Track length is invalid.");
            return;
        }

        int sampleCount = isClosedLoop
            ? Mathf.Max(3, Mathf.CeilToInt(trackLength / sampleSpacing))
            : Mathf.Max(3, Mathf.CeilToInt(trackLength / sampleSpacing) + 1);

        float actualSpacing = isClosedLoop
            ? trackLength / sampleCount
            : trackLength / (sampleCount - 1);

        List<Vector3> positions = new();
        List<float> roadSurfaceHeights = new();

        for (int i = 0; i < sampleCount; i++)
        {
            float distance = isClosedLoop
                ? i * actualSpacing
                : Mathf.Min(i * actualSpacing, trackLength);

            float t = GetTimeAtDistance(
                distance,
                lookupDistances,
                lookupTimes);

            float3 splinePoint = sourceSpline.EvaluatePosition(t);

            Vector3 approximatePosition = new Vector3(
                splinePoint.x,
                splinePoint.y,
                splinePoint.z);

            if (!TryGetRoadSurface(
                approximatePosition,
                out Vector3 roadSurfacePoint))
            {
                if (requireSurfaceHit)
                {
                    Debug.LogError(
                        $"{name}: Could not find the road surface near distance {distance:0.00}. " +
                        "Confirm that the RoadSurface layer and MeshCollider are configured.");

                    return;
                }

                roadSurfacePoint = approximatePosition;
            }

            positions.Add(roadSurfacePoint);
            roadSurfaceHeights.Add(roadSurfacePoint.y);
        }

        SmoothHeights(
            positions,
            roadSurfaceHeights,
            isClosedLoop);

        TrackData.TrackSample[] samples =
            BuildTrackSamples(
                positions,
                roadSurfaceHeights,
                actualSpacing,
                isClosedLoop);

        destinationTrackData.SetBakedData(
            samples,
            trackLength,
            actualSpacing,
            isClosedLoop);

#if UNITY_EDITOR
        EditorUtility.SetDirty(destinationTrackData);
        AssetDatabase.SaveAssets();
#endif

        Debug.Log(
            $"{name}: Baked {samples.Length} gameplay samples " +
            $"across {trackLength:0.0} meters.");
    }

    private void BuildDistanceLookup(
        out List<float> distances,
        out List<float> times,
        out float trackLength)
    {
        distances = new List<float>();
        times = new List<float>();

        float cumulativeDistance = 0f;

        float3 firstPoint = sourceSpline.EvaluatePosition(0f);

        Vector3 previousPosition = new Vector3(
            firstPoint.x,
            firstPoint.y,
            firstPoint.z);

        distances.Add(0f);
        times.Add(0f);

        for (int i = 1; i <= distanceLookupResolution; i++)
        {
            float t = i / (float)distanceLookupResolution;

            float3 splinePoint = sourceSpline.EvaluatePosition(t);

            Vector3 currentPosition = new Vector3(
                splinePoint.x,
                splinePoint.y,
                splinePoint.z);

            cumulativeDistance += Vector3.Distance(
                previousPosition,
                currentPosition);

            distances.Add(cumulativeDistance);
            times.Add(t);

            previousPosition = currentPosition;
        }

        trackLength = cumulativeDistance;
    }

    private float GetTimeAtDistance(
        float targetDistance,
        List<float> lookupDistances,
        List<float> lookupTimes)
    {
        for (int i = 1; i < lookupDistances.Count; i++)
        {
            if (lookupDistances[i] < targetDistance)
            {
                continue;
            }

            float distanceBefore = lookupDistances[i - 1];
            float distanceAfter = lookupDistances[i];

            float lerpAmount = Mathf.InverseLerp(
                distanceBefore,
                distanceAfter,
                targetDistance);

            return Mathf.Lerp(
                lookupTimes[i - 1],
                lookupTimes[i],
                lerpAmount);
        }

        return 1f;
    }

    private bool TryGetRoadSurface(
        Vector3 approximatePosition,
        out Vector3 roadSurfacePoint)
    {
        Vector3 rayOrigin =
            approximatePosition +
            Vector3.up *
            rayStartHeight;

        bool didHit = Physics.Raycast(
            rayOrigin,
            Vector3.down,
            out RaycastHit hit,
            rayDistance,
            roadSurfaceLayer,
            QueryTriggerInteraction.Ignore);

        if (!didHit)
        {
            roadSurfacePoint = approximatePosition;
            return false;
        }

        roadSurfacePoint =
            hit.point +
            hit.normal *
            surfaceClearance;

        return true;
    }

    private void SmoothHeights(
        List<Vector3> positions,
        List<float> roadSurfaceHeights,
        bool isClosedLoop)
    {
        if (smoothingRadius <= 0 ||
            smoothingPasses <= 0 ||
            positions.Count < 3)
        {
            return;
        }

        for (int pass = 0; pass < smoothingPasses; pass++)
        {
            List<Vector3> nextPositions =
                new List<Vector3>(positions);

            for (int i = 0; i < positions.Count; i++)
            {
                float totalHeight = 0f;
                int sampleCount = 0;

                for (int offset = -smoothingRadius;
                    offset <= smoothingRadius;
                    offset++)
                {
                    int sampleIndex = i + offset;

                    if (isClosedLoop)
                    {
                        sampleIndex =
                            (sampleIndex % positions.Count +
                            positions.Count) %
                            positions.Count;
                    }
                    else
                    {
                        sampleIndex = Mathf.Clamp(
                            sampleIndex,
                            0,
                            positions.Count - 1);
                    }

                    totalHeight += positions[sampleIndex].y;
                    sampleCount++;
                }

                float averageHeight =
                    totalHeight /
                    sampleCount;

                Vector3 smoothedPosition =
                    positions[i];

                float smoothedHeight =
                    Mathf.Lerp(
                        smoothedPosition.y,
                        averageHeight,
                        smoothingStrength);

                float minimumAllowedHeight =
                    roadSurfaceHeights[i];

                smoothedPosition.y =
                    Mathf.Max(
                        smoothedHeight,
                        minimumAllowedHeight);

                nextPositions[i] =
                    smoothedPosition;
            }

            positions.Clear();
            positions.AddRange(nextPositions);
        }
    }

    private TrackData.TrackSample[] BuildTrackSamples(
        List<Vector3> positions,
        List<float> roadSurfaceHeights,
        float actualSpacing,
        bool isClosedLoop)
    {
        TrackData.TrackSample[] samples =
            new TrackData.TrackSample[
                positions.Count];

        for (int i = 0; i < positions.Count; i++)
        {
            int previousIndex =
                GetNeighborIndex(
                    i - 1,
                    positions.Count,
                    isClosedLoop);

            int nextIndex =
                GetNeighborIndex(
                    i + 1,
                    positions.Count,
                    isClosedLoop);

            Vector3 previousPosition =
                positions[previousIndex];

            Vector3 currentPosition =
                positions[i];

            Vector3 nextPosition =
                positions[nextIndex];

            Vector3 forward =
                nextPosition -
                previousPosition;

            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();

            float horizontalDistance =
                Vector2.Distance(
                    new Vector2(
                        previousPosition.x,
                        previousPosition.z),
                    new Vector2(
                        nextPosition.x,
                        nextPosition.z));

            float slopePercent =
                horizontalDistance <= 0.001f
                    ? 0f
                    : (nextPosition.y -
                       previousPosition.y) /
                      horizontalDistance *
                      100f;

            samples[i] =
                new TrackData.TrackSample
                {
                    distance =
                        i * actualSpacing,

                    position =
                        currentPosition,

                    forward =
                        forward,

                    up =
                        Vector3.up,

                    slopePercent =
                        slopePercent,

                    roadSurfaceHeight =
                        roadSurfaceHeights[i]
                };
        }

        return samples;
    }

    private int GetNeighborIndex(
        int index,
        int count,
        bool isClosedLoop)
    {
        if (isClosedLoop)
        {
            return
                (index % count + count) %
                count;
        }

        return Mathf.Clamp(
            index,
            0,
            count - 1);
    }
}