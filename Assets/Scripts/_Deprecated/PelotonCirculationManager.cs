using System.Collections.Generic;
using UnityEngine;

public class PelotonCirculationManager : MonoBehaviour
{
    [Header("Rotation Timing")]
    [Tooltip("Minimum seconds before the front rider rotates out.")]
    [SerializeField, Min(1f)] private float minimumPullDuration = 7f;

    [Tooltip("Maximum seconds before the front rider rotates out.")]
    [SerializeField, Min(1f)] private float maximumPullDuration = 11f;

    [Tooltip("How long the old leader eases backward through the pack.")]
    [SerializeField, Min(1f)] private float retiringDuration = 5f;

    [Header("Target Speeds")]
    [Tooltip("Pace maintained by the current front rider.")]
    [SerializeField, Range(0f, 1f)] private float pullingSpeedFraction = 0.68f;

    [Tooltip("Slightly faster pace used by the rider moving up to the front.")]
    [SerializeField, Range(0f, 1f)] private float advancingSpeedFraction = 0.71f;

    [Tooltip("Reduced pace used by the rider rotating backward.")]
    [SerializeField, Range(0f, 1f)] private float retiringSpeedFraction = 0.60f;

    [Header("Candidate Selection")]
    [Tooltip("Only riders near the front of the peloton can become the next puller.")]
    [SerializeField, Min(2)] private int frontCandidateCount = 14;

    [Tooltip("How far behind the current leader the next puller may be selected.")]
    [SerializeField, Min(1f)] private float maximumAdvanceDistance = 18f;

    [Header("Lane Movement")]
    [Tooltip("Lane direction used by riders advancing toward the front.")]
    [SerializeField] private int advancingLaneDirection = -1;

    [Tooltip("Lane direction used by riders rotating backward.")]
    [SerializeField] private int retiringLaneDirection = 1;

    [Header("Debug")]
    [SerializeField] private string currentPullerName = "None";
    [SerializeField] private string advancingRiderName = "None";
    [SerializeField] private string retiringRiderName = "None";

    private readonly List<NPCRacerController> orderedCandidates = new();

    private NPCRacerController currentPuller;
    private NPCRacerController advancingRider;
    private NPCRacerController retiringRider;

    private float nextRotationTime;

    private void Start()
    {
        ScheduleNextRotation();
    }

    private void Update()
    {
        if (Time.time < nextRotationTime)
        {
            return;
        }

        RotateFrontRiders();
        ScheduleNextRotation();
    }

    private void RotateFrontRiders()
    {
        RefreshCandidates();

        if (orderedCandidates.Count == 0)
        {
            return;
        }

        NPCRacerController previousPuller = currentPuller;

        if (advancingRider != null &&
            advancingRider.IsEligibleForPelotonCirculation)
        {
            currentPuller = advancingRider;
        }
        else
        {
            currentPuller = orderedCandidates[0];
        }

        currentPuller.SetCirculationOverride(
            pullingSpeedFraction,
            maximumPullDuration,
            false);

        currentPullerName = currentPuller.name;

        if (previousPuller != null &&
            previousPuller != currentPuller)
        {
            retiringRider = previousPuller;

            retiringRider.SetCirculationOverride(
                retiringSpeedFraction,
                retiringDuration,
                true);

            TryMoveOneLane(
                retiringRider,
                retiringLaneDirection);

            retiringRiderName =
                retiringRider.name;
        }
        else
        {
            retiringRiderName = "None";
        }

        advancingRider =
            FindNextAdvancingRider();

        if (advancingRider != null)
        {
            advancingRider.SetCirculationOverride(
                advancingSpeedFraction,
                maximumPullDuration,
                false);

            TryMoveOneLane(
                advancingRider,
                advancingLaneDirection);

            advancingRiderName =
                advancingRider.name;
        }
        else
        {
            advancingRiderName = "None";
        }
    }

    private void RefreshCandidates()
    {
        orderedCandidates.Clear();

        NPCRacerController[] allNPCs =
            FindObjectsByType<NPCRacerController>(
                FindObjectsSortMode.None);

        foreach (NPCRacerController npc in allNPCs)
        {
            if (npc == null ||
                !npc.IsEligibleForPelotonCirculation)
            {
                continue;
            }

            orderedCandidates.Add(npc);
        }

        orderedCandidates.Sort(
            (first, second) =>
                second.RaceDistance.CompareTo(
                    first.RaceDistance));

        if (orderedCandidates.Count >
            frontCandidateCount)
        {
            orderedCandidates.RemoveRange(
                frontCandidateCount,
                orderedCandidates.Count -
                frontCandidateCount);
        }
    }

    private NPCRacerController FindNextAdvancingRider()
    {
        if (currentPuller == null)
        {
            return null;
        }

        foreach (NPCRacerController candidate in orderedCandidates)
        {
            if (candidate == null ||
                candidate == currentPuller ||
                candidate == retiringRider)
            {
                continue;
            }

            float distanceBehind =
                currentPuller.RaceDistance -
                candidate.RaceDistance;

            if (distanceBehind < 0f ||
                distanceBehind >
                maximumAdvanceDistance)
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private void TryMoveOneLane(
        NPCRacerController rider,
        int direction)
    {
        if (rider == null ||
            rider.Motor == null ||
            direction == 0)
        {
            return;
        }

        rider.Motor.ChangeLane(
            direction > 0 ? 1 : -1);
    }

    private void ScheduleNextRotation()
    {
        nextRotationTime =
            Time.time +
            Random.Range(
                minimumPullDuration,
                maximumPullDuration);
    }
}