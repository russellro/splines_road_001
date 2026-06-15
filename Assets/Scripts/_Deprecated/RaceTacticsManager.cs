using System.Collections.Generic;
using UnityEngine;

public class RaceTacticsManager : MonoBehaviour
{
    [Header("Refresh")]
    [SerializeField, Min(0.1f)] private float refreshInterval = 0.5f;

    [Header("Breakaway Timing")]
    [Tooltip("Seconds after the race begins before the first breakaway forms.")]
    [SerializeField, Min(0f)] private float firstBreakawayDelay = 10f;

    [Tooltip("Seconds after the breakaway begins before a chase group forms.")]
    [SerializeField, Min(0f)] private float chaseGroupDelay = 7f;

    [Header("Breakaway")]
    [SerializeField, Min(0)] private int breakawayFollowerCount = 3;

    [Tooltip("Only riders near the front can initiate the breakaway.")]
    [SerializeField, Min(1)] private int frontCandidateCount = 12;

    [SerializeField, Range(0f, 1f)] private float minimumBreakawayATP = 0.55f;

    [SerializeField, Range(0f, 1f)] private float breakawayLeaderSpeed = 0.90f;
    [SerializeField, Range(0f, 1f)] private float breakawayFollowerSpeed = 0.84f;

    [Header("Chase Group")]
    [SerializeField, Min(0)] private int chaseGroupSize = 8;
    [SerializeField, Range(0f, 1f)] private float chaseGroupSpeed = 0.74f;

    [Header("Peloton")]
    [SerializeField, Range(0f, 1f)] private float pelotonFrontSpeed = 0.68f;
    [SerializeField, Range(0f, 1f)] private float pelotonBodySpeed = 0.62f;

    [Header("Debug")]
    [SerializeField] private string breakawayLeaderName = "None";
    [SerializeField] private int currentBreakawaySize;
    [SerializeField] private int currentChaseSize;
    [SerializeField] private string pelotonFrontName = "None";

    private readonly List<NPCRacerController> npcs = new();
    private readonly List<NPCRacerController> orderedRiders = new();
    private readonly List<NPCRacerController> breakawayGroup = new();
    private readonly List<NPCRacerController> chaseGroup = new();

    private NPCRacerController breakawayLeader;
    private NPCRacerController pelotonFront;

    private float nextRefreshTime;
    private float raceStartTime;
    private float chaseStartTime;

    private bool breakawayStarted;
    private bool chaseStarted;

    private void Start()
    {
        raceStartTime = Time.time;
        CacheNPCs();
        ApplyStrategicOrders();
    }

    private void Update()
    {
        if (Time.time < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime =
            Time.time +
            refreshInterval;

        RemoveMissingRiders();
        SortRidersFrontToBack();

        if (!breakawayStarted &&
            Time.time >=
            raceStartTime +
            firstBreakawayDelay)
        {
            StartBreakaway();
        }

        if (breakawayStarted &&
            !chaseStarted &&
            Time.time >=
            chaseStartTime)
        {
            StartChaseGroup();
        }

        ApplyStrategicOrders();
    }

    private void CacheNPCs()
    {
        npcs.Clear();

        NPCRacerController[] discoveredNPCs =
            FindObjectsByType<NPCRacerController>(
                FindObjectsSortMode.None);

        npcs.AddRange(
            discoveredNPCs);

        SortRidersFrontToBack();
    }

    private void RemoveMissingRiders()
    {
        npcs.RemoveAll(
            npc => npc == null);

        breakawayGroup.RemoveAll(
            npc => npc == null);

        chaseGroup.RemoveAll(
            npc => npc == null);
    }

    private void SortRidersFrontToBack()
    {
        orderedRiders.Clear();
        orderedRiders.AddRange(npcs);

        orderedRiders.Sort(
            (first, second) =>
                second.RaceDistance.CompareTo(
                    first.RaceDistance));
    }

    private void StartBreakaway()
    {
        List<NPCRacerController> candidates =
            new();

        int candidatesToCheck =
            Mathf.Min(
                frontCandidateCount,
                orderedRiders.Count);

        for (int i = 0;
            i < candidatesToCheck;
            i++)
        {
            NPCRacerController rider =
                orderedRiders[i];

            if (rider != null &&
                rider.ATPFraction >=
                minimumBreakawayATP)
            {
                candidates.Add(
                    rider);
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        breakawayLeader =
            candidates[
                Random.Range(
                    0,
                    candidates.Count)];

        breakawayGroup.Clear();
        breakawayGroup.Add(
            breakawayLeader);

        foreach (NPCRacerController rider in orderedRiders)
        {
            if (rider == null ||
                rider ==
                breakawayLeader)
            {
                continue;
            }

            float distanceBehindLeader =
                breakawayLeader.RaceDistance -
                rider.RaceDistance;

            if (distanceBehindLeader < 0f ||
                distanceBehindLeader > 16f)
            {
                continue;
            }

            if (rider.ATPFraction <
                minimumBreakawayATP *
                0.80f)
            {
                continue;
            }

            breakawayGroup.Add(
                rider);

            if (breakawayGroup.Count >=
                breakawayFollowerCount + 1)
            {
                break;
            }
        }

        breakawayStarted = true;

        chaseStartTime =
            Time.time +
            chaseGroupDelay;

        breakawayLeaderName =
            breakawayLeader.name;
    }

    private void StartChaseGroup()
    {
        chaseGroup.Clear();

        foreach (NPCRacerController rider in orderedRiders)
        {
            if (rider == null ||
                breakawayGroup.Contains(
                    rider))
            {
                continue;
            }

            chaseGroup.Add(
                rider);

            if (chaseGroup.Count >=
                chaseGroupSize)
            {
                break;
            }
        }

        chaseStarted = true;
    }

    private void ApplyStrategicOrders()
    {
        foreach (NPCRacerController rider in npcs)
        {
            if (rider == null)
            {
                continue;
            }

            rider.SetStrategicOrder(
                NPCRacerController.StrategicRole.PelotonBody,
                pelotonBodySpeed,
                false);
        }

        foreach (NPCRacerController rider in breakawayGroup)
        {
            if (rider == null)
            {
                continue;
            }

            bool isLeader =
                rider ==
                breakawayLeader;

            rider.SetStrategicOrder(
                isLeader
                    ? NPCRacerController.StrategicRole.BreakawayLeader
                    : NPCRacerController.StrategicRole.BreakawayFollower,
                isLeader
                    ? breakawayLeaderSpeed
                    : breakawayFollowerSpeed,
                true);
        }

        foreach (NPCRacerController rider in chaseGroup)
        {
            if (rider == null ||
                breakawayGroup.Contains(
                    rider))
            {
                continue;
            }

            rider.SetStrategicOrder(
                NPCRacerController.StrategicRole.ChaseGroup,
                chaseGroupSpeed,
                true);
        }

        pelotonFront =
            FindFirstAvailablePelotonRider();

        if (pelotonFront != null)
        {
            pelotonFront.SetStrategicOrder(
                NPCRacerController.StrategicRole.PelotonFront,
                pelotonFrontSpeed,
                true);

            pelotonFrontName =
                pelotonFront.name;
        }
        else
        {
            pelotonFrontName =
                "None";
        }

        currentBreakawaySize =
            breakawayGroup.Count;

        currentChaseSize =
            chaseGroup.Count;
    }

    private NPCRacerController
        FindFirstAvailablePelotonRider()
    {
        foreach (NPCRacerController rider in orderedRiders)
        {
            if (rider == null ||
                breakawayGroup.Contains(
                    rider) ||
                chaseGroup.Contains(
                    rider))
            {
                continue;
            }

            return rider;
        }

        return null;
    }
}