using System;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Collections;
using Unity.Mathematics;

public class TrackPath : MonoBehaviour
{
    [Header("Spline")]
    [SerializeField] private SplineContainer splineContainer;

    [Header("Baked Gameplay Track")]
    [Tooltip("Smooth baked gameplay data used by racers at runtime.")]
    [SerializeField]
    private TrackData trackData;

    [Header("Lane Settings")]
    [SerializeField, Min(1)] private int laneCount = 7;

    [SerializeField, Min(0.01f)]
    private float laneSpacing = 1.5f;

    [Header("Distance Accuracy")]
    [Tooltip("Higher values improve distance accuracy on curves. 2048 is a good starting point.")]
    [SerializeField, Min(64)]
    private int distanceTableResolution = 2048;

    [Header("Road Surface Offset")]
    [Tooltip("Raises racers above the spline so they sit on top of the road mesh. Use 0 for a correctly baked gameplay rail.")]
    [SerializeField]
    private float roadSurfaceOffset = 0f;

    [Header("Grade Smoothing")]
    [Tooltip(
    "Number of nearby baked samples averaged when calculating grade. " +
    "With one-meter samples, a radius of 4 averages roughly nine meters.")]
    [SerializeField, Range(0, 20)]
    private int slopeSmoothingSampleRadius = 4;

    private NativeSpline worldSpline;
    private bool splineCacheIsReady;

    private float[] distanceSamples;
    private float[] progressSamples;

    public int LaneCount => laneCount;
    public float LaneSpacing => laneSpacing;
    public float Length { get; private set; }

    private bool HasValidTrackData =>
        trackData != null &&
        trackData.Samples != null &&
        trackData.Samples.Length >= 3 &&
        trackData.TrackLength > 0f;

    private void OnEnable()
    {
        RebuildSplineCache();
    }

    private void OnDisable()
    {
        DisposeSplineCache();
    }

    private void OnValidate()
    {
        laneCount =
            Mathf.Max(
                1,
                laneCount);

        laneSpacing =
            Mathf.Max(
                0.01f,
                laneSpacing);

        distanceTableResolution =
            Mathf.Max(
                64,
                distanceTableResolution);
    }

    public int ClampLane(int lane)
    {
        return Mathf.Clamp(
            lane,
            0,
            laneCount - 1);
    }

    public float GetLaneOffset(int lane)
    {
        int safeLane =
            ClampLane(
                lane);

        float centerLane =
            (laneCount - 1) /
            2f;

        return
            (safeLane -
            centerLane) *
            laneSpacing;
    }

    public float GetProgressAtDistance(float distance)
    {
        if (HasValidTrackData)
        {
            return Mathf.Repeat(
                distance,
                trackData.TrackLength) /
                trackData.TrackLength;
        }

        if (!splineCacheIsReady ||
            Length <= 0f)
        {
            return 0f;
        }

        float wrappedDistance =
            Mathf.Repeat(
                distance,
                Length);

        int index =
            Array.BinarySearch(
                distanceSamples,
                wrappedDistance);

        if (index >= 0)
        {
            return progressSamples[
                index];
        }

        int nextIndex =
            ~index;

        int previousIndex =
            Mathf.Max(
                0,
                nextIndex - 1);

        nextIndex =
            Mathf.Min(
                nextIndex,
                distanceSamples.Length -
                1);

        float previousDistance =
            distanceSamples[
                previousIndex];

        float nextDistance =
            distanceSamples[
                nextIndex];

        float distanceRange =
            nextDistance -
            previousDistance;

        if (distanceRange <= 0.0001f)
        {
            return progressSamples[
                previousIndex];
        }

        float interpolation =
            (wrappedDistance -
            previousDistance) /
            distanceRange;

        return Mathf.Lerp(
            progressSamples[
                previousIndex],
            progressSamples[
                nextIndex],
            interpolation);
    }

    public float GetDistanceAtProgress(float normalizedProgress)
    {
        if (HasValidTrackData)
        {
            return Mathf.Repeat(
                normalizedProgress,
                1f) *
                trackData.TrackLength;
        }

        if (!splineCacheIsReady ||
            Length <= 0f)
        {
            return 0f;
        }

        normalizedProgress =
            Mathf.Repeat(
                normalizedProgress,
                1f);

        float samplePosition =
            normalizedProgress *
            distanceTableResolution;

        int previousIndex =
            Mathf.FloorToInt(
                samplePosition);

        int nextIndex =
            Mathf.Min(
                previousIndex + 1,
                distanceSamples.Length -
                1);

        float interpolation =
            samplePosition -
            previousIndex;

        return Mathf.Lerp(
            distanceSamples[
                previousIndex],
            distanceSamples[
                nextIndex],
            interpolation);
    }

    public bool TryGetPose(
        float normalizedProgress,
        float sidewaysOffset,
        out Vector3 position,
        out Quaternion rotation)
    {
        if (HasValidTrackData)
        {
            float distance =
                Mathf.Repeat(
                    normalizedProgress,
                    1f) *
                trackData.TrackLength;

            return TryGetBakedPoseAtDistance(
                distance,
                sidewaysOffset,
                out position,
                out rotation);
        }

        position =
            Vector3.zero;

        rotation =
            Quaternion.identity;

        if (!splineCacheIsReady)
        {
            return false;
        }

        normalizedProgress =
            Mathf.Repeat(
                normalizedProgress,
                1f);

        bool validPose =
            worldSpline.Evaluate(
                normalizedProgress,
                out float3 centerPosition,
                out float3 tangentValue,
                out float3 upValue);

        if (!validPose)
        {
            return false;
        }

        Vector3 forward =
            ToVector3(
                tangentValue);

        Vector3 up =
            ToVector3(
                upValue);

        if (forward.sqrMagnitude <
            0.0001f)
        {
            const float progressOffset =
                0.0001f;

            float previousProgress =
                Mathf.Repeat(
                    normalizedProgress -
                    progressOffset,
                    1f);

            float nextProgress =
                Mathf.Repeat(
                    normalizedProgress +
                    progressOffset,
                    1f);

            Vector3 previousPosition =
                ToVector3(
                    worldSpline.EvaluatePosition(
                        previousProgress));

            Vector3 nextPosition =
                ToVector3(
                    worldSpline.EvaluatePosition(
                        nextProgress));

            forward =
                nextPosition -
                previousPosition;
        }

        if (forward.sqrMagnitude <
            0.0001f)
        {
            return false;
        }

        forward.Normalize();

        if (up.sqrMagnitude <
            0.0001f)
        {
            up =
                Vector3.up;
        }

        up.Normalize();

        Vector3 sideways =
            Vector3.Cross(
                up,
                forward).normalized;

        position =
            ToVector3(
                centerPosition) +
            sideways *
            sidewaysOffset +
            up *
            roadSurfaceOffset;

        rotation =
            Quaternion.LookRotation(
                forward,
                up);

        return true;
    }

    [ContextMenu("Rebuild Spline Cache")]
    public void RebuildSplineCache()
    {
        DisposeSplineCache();

        if (HasValidTrackData)
        {
            Length =
                trackData.TrackLength;

            Debug.Log(
                $"{name}: Loaded baked gameplay track with " +
                $"{trackData.SampleCount} samples across " +
                $"{Length:0.0} meters.");

            return;
        }

        if (splineContainer == null)
        {
            Debug.LogError(
                $"{name}: No SplineContainer has been assigned.");

            return;
        }

        float4x4 worldMatrix =
            ConvertToFloat4x4(
                splineContainer.transform.localToWorldMatrix);

        worldSpline =
            new NativeSpline(
                splineContainer.Spline,
                worldMatrix,
                Allocator.Persistent);

        BuildDistanceTable();

        splineCacheIsReady =
            true;
    }

    private void BuildDistanceTable()
    {
        int sampleCount =
            distanceTableResolution +
            1;

        distanceSamples =
            new float[
                sampleCount];

        progressSamples =
            new float[
                sampleCount];

        float totalDistance =
            0f;

        Vector3 previousPosition =
            ToVector3(
                worldSpline.EvaluatePosition(
                    0f));

        distanceSamples[0] =
            0f;

        progressSamples[0] =
            0f;

        for (int i = 1;
            i < sampleCount;
            i++)
        {
            float progress =
                (float)i /
                distanceTableResolution;

            Vector3 currentPosition =
                ToVector3(
                    worldSpline.EvaluatePosition(
                        progress));

            totalDistance +=
                Vector3.Distance(
                    previousPosition,
                    currentPosition);

            distanceSamples[i] =
                totalDistance;

            progressSamples[i] =
                progress;

            previousPosition =
                currentPosition;
        }

        Length =
            totalDistance;
    }

    private void DisposeSplineCache()
    {
        if (!splineCacheIsReady)
        {
            return;
        }

        worldSpline.Dispose();

        splineCacheIsReady =
            false;

        distanceSamples =
            null;

        progressSamples =
            null;

        Length =
            0f;
    }

    private static Vector3 ToVector3(float3 value)
    {
        return new Vector3(
            value.x,
            value.y,
            value.z);
    }

    private static float4x4 ConvertToFloat4x4(
        Matrix4x4 matrix)
    {
        return new float4x4(
            new float4(
                matrix.m00,
                matrix.m10,
                matrix.m20,
                matrix.m30),

            new float4(
                matrix.m01,
                matrix.m11,
                matrix.m21,
                matrix.m31),

            new float4(
                matrix.m02,
                matrix.m12,
                matrix.m22,
                matrix.m32),

            new float4(
                matrix.m03,
                matrix.m13,
                matrix.m23,
                matrix.m33));
    }

    private bool TryGetBakedPoseAtDistance(
        float distance,
        float sidewaysOffset,
        out Vector3 position,
        out Quaternion rotation)
    {
        position =
            Vector3.zero;

        rotation =
            Quaternion.identity;

        if (!HasValidTrackData)
        {
            return false;
        }

        TrackData.TrackSample[] samples =
            trackData.Samples;

        int sampleCount =
            samples.Length;

        float wrappedDistance =
            Mathf.Repeat(
                distance,
                trackData.TrackLength);

        float sampleProgress =
            wrappedDistance /
            trackData.SampleSpacing;

        int firstIndex =
            Mathf.FloorToInt(
                sampleProgress) %
            sampleCount;

        int secondIndex =
            (firstIndex + 1) %
            sampleCount;

        float interpolation =
            sampleProgress -
            Mathf.Floor(
                sampleProgress);

        TrackData.TrackSample firstSample =
            samples[
                firstIndex];

        TrackData.TrackSample secondSample =
            samples[
                secondIndex];

        Vector3 centerPosition =
            Vector3.Lerp(
                firstSample.position,
                secondSample.position,
                interpolation);

        Vector3 forward =
            Vector3.Slerp(
                firstSample.forward,
                secondSample.forward,
                interpolation);

        if (forward.sqrMagnitude <
            0.0001f)
        {
            forward =
                Vector3.forward;
        }

        forward.Normalize();

        Vector3 sideways =
            Vector3.Cross(
                Vector3.up,
                forward);

        if (sideways.sqrMagnitude <
            0.0001f)
        {
            sideways =
                Vector3.right;
        }

        sideways.Normalize();

        position =
            centerPosition +
            sideways *
            sidewaysOffset +
            Vector3.up *
            roadSurfaceOffset;

        rotation =
            Quaternion.LookRotation(
                forward,
                Vector3.up);

        return true;
    }

    public float GetSlopePercentAtDistance(float distance)
    {
        if (!HasValidTrackData)
        {
            return 0f;
        }

        TrackData.TrackSample[] samples =
            trackData.Samples;

        float wrappedDistance =
            Mathf.Repeat(
                distance,
                trackData.TrackLength);

        float sampleProgress =
            wrappedDistance /
            trackData.SampleSpacing;

        int firstIndex =
            Mathf.FloorToInt(
                sampleProgress) %
            samples.Length;

        int secondIndex =
            (firstIndex + 1) %
            samples.Length;

        float interpolation =
            sampleProgress -
            Mathf.Floor(
                sampleProgress);

        float firstSlope =
            GetSmoothedSlopeAtIndex(
                firstIndex,
                samples);

        float secondSlope =
            GetSmoothedSlopeAtIndex(
                secondIndex,
                samples);

        return Mathf.Lerp(
            firstSlope,
            secondSlope,
            interpolation);
    }

    private float GetSmoothedSlopeAtIndex(
    int centerIndex,
    TrackData.TrackSample[] samples)
    {
        if (slopeSmoothingSampleRadius <= 0)
        {
            return samples[
                centerIndex].slopePercent;
        }

        float weightedSlopeTotal = 0f;
        float totalWeight = 0f;

        for (int offset =
                -slopeSmoothingSampleRadius;
            offset <=
                slopeSmoothingSampleRadius;
            offset++)
        {
            int sampleIndex =
                centerIndex +
                offset;

            if (trackData.IsClosedLoop)
            {
                sampleIndex =
                    (sampleIndex %
                    samples.Length +
                    samples.Length) %
                    samples.Length;
            }
            else
            {
                sampleIndex =
                    Mathf.Clamp(
                        sampleIndex,
                        0,
                        samples.Length - 1);
            }

            float weight =
                slopeSmoothingSampleRadius +
                1 -
                Mathf.Abs(
                    offset);

            weightedSlopeTotal +=
                samples[
                    sampleIndex].
                    slopePercent *
                weight;

            totalWeight +=
                weight;
        }

        return totalWeight <= 0f
            ? samples[
                centerIndex].
                slopePercent
            : weightedSlopeTotal /
                totalWeight;
    }
}
