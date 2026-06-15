using System;
using UnityEngine;

[CreateAssetMenu(
    fileName = "TrackData",
    menuName = "Cycling Game/Track Data")]
public class TrackData : ScriptableObject
{
    [Serializable]
    public struct TrackSample
    {
        public float distance;
        public Vector3 position;
        public Vector3 forward;
        public Vector3 up;
        public float slopePercent;
        public float roadSurfaceHeight;
    }

    [Header("Baked Track")]
    [SerializeField] private TrackSample[] samples;
    [SerializeField] private float trackLength;
    [SerializeField] private float sampleSpacing = 1f;
    [SerializeField] private bool isClosedLoop = true;

    public TrackSample[] Samples => samples;
    public float TrackLength => trackLength;
    public float SampleSpacing => sampleSpacing;
    public bool IsClosedLoop => isClosedLoop;
    public int SampleCount => samples == null ? 0 : samples.Length;

    public void SetBakedData(
        TrackSample[] newSamples,
        float newTrackLength,
        float newSampleSpacing,
        bool newIsClosedLoop)
    {
        samples = newSamples;
        trackLength = Mathf.Max(0f, newTrackLength);
        sampleSpacing = Mathf.Max(0.01f, newSampleSpacing);
        isClosedLoop = newIsClosedLoop;
    }
}