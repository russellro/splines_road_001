using System.Collections.Generic;
using UnityEngine;

public class PelotonManager : MonoBehaviour
{
    public struct LocalPackSample
    {
        public int NeighborCount;
        public float AverageSpeed;
        public float AverageDistance;
    }

    private sealed class DistanceComparer :
        IComparer<NPCRacerController>
    {
        public int Compare(
            NPCRacerController a,
            NPCRacerController b)
        {
            if (ReferenceEquals(a, b))
            {
                return 0;
            }

            if (a == null)
            {
                return -1;
            }

            if (b == null)
            {
                return 1;
            }

            return a.RaceDistance.CompareTo(
                b.RaceDistance);
        }
    }

    [Header("Refresh")]
    [SerializeField, Min(0.05f)]
    private float refreshInterval = 0.25f;

    [Header("Local Pack")]
    [Tooltip("How far ahead and behind a racer to look for nearby pack riders.")]
    [SerializeField, Min(1f)]
    private float localPackRadius = 14f;

    [Tooltip("Minimum nearby riders needed before cohesion behavior activates.")]
    [SerializeField, Min(1)]
    private int minimumNeighborsForPack = 3;

    [Header("Cohesion")]
    [Tooltip("How far a rider can drift from the local pack center before receiving the maximum correction.")]
    [SerializeField, Min(1f)]
    private float cohesionDistance = 10f;

    [Tooltip("Maximum speed-fraction adjustment used to pull riders toward the pack.")]
    [SerializeField, Range(0f, 0.5f)]
    private float maximumCohesionAdjustment = 0.12f;

    [Header("Automatic Strategic Roles")]
    [Tooltip(
        "When enabled, this manager identifies the largest connected pack as " +
        "the main peloton, limits how many riders can remain detached ahead, " +
        "and assigns the ChaseGroup role to the permitted detached riders.")]
    [SerializeField]
    private bool automaticallyAssignStrategicRoles = true;

    [Tooltip(
        "Maximum longitudinal gap allowed between consecutive riders while " +
        "detecting the main peloton. Increase this slightly if the main pack " +
        "is incorrectly split into multiple groups.")]
    [SerializeField, Min(0.5f)]
    private float mainPelotonLinkDistance = 9f;

    [Tooltip(
        "A rider must be at least this far ahead of the detected peloton front " +
        "before being treated as detached.")]
    [SerializeField, Min(0f)]
    private float detachedRiderMinimumGap = 8f;

    [Tooltip(
        "Maximum number of detached riders allowed to remain in chase groups. " +
        "Additional riders are slowed so the main peloton can reabsorb them.")]
    [SerializeField, Min(0)]
    private int maximumDetachedChaseRiders = 18;

    [Tooltip(
        "Number of riders at the front of the detected peloton permitted to " +
        "circulate and make normal passes.")]
    [SerializeField, Min(0)]
    private int pelotonFrontRiderCount = 8;

    [Header("Strategic Role Speeds")]
    [Tooltip("Target speed fraction assigned to ordinary peloton riders.")]
    [SerializeField, Range(0f, 1f)]
    private float pelotonBodySpeedFraction = 0.56f;

    [Tooltip("Target speed fraction assigned to riders near the peloton front.")]
    [SerializeField, Range(0f, 1f)]
    private float pelotonFrontSpeedFraction = 0.59f;

    [Tooltip("Target speed fraction assigned to permitted detached chase riders.")]
    [SerializeField, Range(0f, 1f)]
    private float chaseGroupSpeedFraction = 0.62f;

    [Tooltip(
        "Temporary speed fraction used to let excess detached riders fall " +
        "back toward the peloton.")]
    [SerializeField, Range(0f, 1f)]
    private float overflowReturnSpeedFraction = 0.46f;

    [Tooltip(
        "Overflow riders are kept at least this much slower than the " +
        "detected main-peloton road speed. This makes return behavior work " +
        "on climbs and descents rather than relying only on a fixed value.")]
    [SerializeField, Range(0f, 0.5f)]
    private float overflowReturnSpeedMargin = 0.10f;

    [Tooltip(
        "Lower safety limit for the dynamically calculated overflow return " +
        "speed. This prevents a steep climb from reducing the target to zero.")]
    [SerializeField, Range(0f, 1f)]
    private float minimumOverflowReturnSpeedFraction = 0.20f;

    [Header("Player Breakaway Response")]
    [Tooltip(
        "When enabled, the main peloton and detached chase groups gradually " +
        "increase their pace when the player opens a meaningful solo gap.")]
    [SerializeField]
    private bool respondToPlayerBreakaway = true;

    [Tooltip(
        "Optional explicit player reference. When empty, the manager finds " +
        "the first active RacerMotor without an NPCRacerController.")]
    [SerializeField]
    private RacerMotor playerMotor;

    [Tooltip(
        "Player gap ahead of the peloton front before an organized pursuit " +
        "begins. Small moves are allowed without an immediate full response.")]
    [SerializeField, Min(0f)]
    private float playerBreakawayResponseStartsAtGap = 10f;

    [Tooltip(
        "Player gap at which the organized pursuit reaches full strength.")]
    [SerializeField, Min(0.1f)]
    private float playerBreakawayFullResponseGap = 45f;

    [Tooltip(
        "Maximum extra speed fraction assigned to ordinary peloton riders " +
        "during a full pursuit response.")]
    [SerializeField, Range(0f, 0.5f)]
    private float maximumPelotonBodyPursuitBoost = 0.08f;

    [Tooltip(
        "Maximum extra speed fraction assigned to riders circulating at the " +
        "peloton front during a full pursuit response.")]
    [SerializeField, Range(0f, 0.5f)]
    private float maximumPelotonFrontPursuitBoost = 0.14f;

    [Tooltip(
        "Maximum extra speed fraction assigned to detached chase riders " +
        "during a full pursuit response.")]
    [SerializeField, Range(0f, 0.5f)]
    private float maximumDetachedChasePursuitBoost = 0.18f;

    [Tooltip(
        "Additional chase-group slots released gradually as the player gap " +
        "grows. This lets a credible pursuit group form behind a breakaway.")]
    [SerializeField, Min(0)]
    private int additionalChaseRidersAtFullResponse = 8;

    [Tooltip(
        "How quickly the pursuit response rises and falls. Lower values make " +
        "the peloton react more gradually rather than snapping to a new pace.")]
    [SerializeField, Min(0f)]
    private float pursuitResponseChangePerSecond = 0.45f;

    [Header("Coordinated Attack Limits")]
    [Tooltip(
        "When enabled, NPC attack requests are admitted through a quota so " +
        "one attack does not pull dozens of riders out of the peloton.")]
    [SerializeField]
    private bool limitCoordinatedAttacks = true;

    [Tooltip("Maximum number of simultaneously committed attack leaders.")]
    [SerializeField, Min(1)]
    private int maximumActiveAttackLeaders = 1;

    [Tooltip("Maximum number of riders simultaneously allowed to follow attacks.")]
    [SerializeField, Min(0)]
    private int maximumActiveAttackFollowers = 8;

    [Tooltip("Minimum time between the starts of separate attacks.")]
    [SerializeField, Min(0f)]
    private float attackStartCooldownSeconds = 9f;

    [Header("Strategy Ownership")]
    [Tooltip("Turn this off when RaceTacticsManager is controlling breakaway, chase, and peloton roles.")]
    [SerializeField] private bool assignStrategicRoles = false;

    [Header("Debug")]
    [SerializeField] private float detectedPelotonRearDistance;
    [SerializeField] private float detectedPelotonFrontDistance;
    [SerializeField] private int detectedPelotonRiderCount;
    [SerializeField] private int detectedDetachedRiderCount;
    [SerializeField] private int permittedDetachedRiderCount;
    [SerializeField] private int overflowDetachedRiderCount;
    [SerializeField] private int activeAttackLeaderCount;
    [SerializeField] private int activeAttackFollowerCount;
    [SerializeField] private float detectedPelotonAverageSpeedFraction;
    [SerializeField] private float currentOverflowReturnTargetSpeedFraction;
    [SerializeField] private float detectedPlayerGapAhead;
    [SerializeField, Range(0f, 1f)] private float currentPlayerBreakawayResponse;
    [SerializeField] private int currentMaximumDetachedChaseRiders;
    [SerializeField] private float currentPelotonBodyTargetSpeedFraction;
    [SerializeField] private float currentPelotonFrontTargetSpeedFraction;
    [SerializeField] private float currentChaseGroupTargetSpeedFraction;

    private readonly Dictionary<RacerMotor, LocalPackSample>
        samples = new();

    private readonly Dictionary<NPCRacerController, float>
        attackLeaderReservations = new();

    private readonly Dictionary<NPCRacerController, float>
        attackFollowerReservations = new();

    private readonly List<NPCRacerController>
        npcRacers = new();

    private readonly List<NPCRacerController>
        detachedRacers = new();

    private readonly List<NPCRacerController>
        mainPelotonRacers = new();

    private readonly DistanceComparer
        distanceComparer = new();

    private float nextRefreshTime;
    private float nextAttackStartTime;

    private void Update()
    {
        if (Time.time < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime =
            Time.time +
            refreshInterval;

        RefreshSamples();
        RemoveExpiredAttackReservations();
        FindPlayerMotorIfMissing();

        if (automaticallyAssignStrategicRoles)
        {
            RefreshStrategicRoles();
        }
    }

    public bool IsInLocalPack(RacerMotor racer)
    {
        return racer != null &&
            samples.TryGetValue(
                racer,
                out LocalPackSample sample) &&
            sample.NeighborCount >=
            minimumNeighborsForPack;
    }

    public int GetLocalDensity(RacerMotor racer)
    {
        return racer != null &&
            samples.TryGetValue(
                racer,
                out LocalPackSample sample)
            ? sample.NeighborCount
            : 0;
    }

    public float GetLocalPackSpeed(RacerMotor racer)
    {
        return racer != null &&
            samples.TryGetValue(
                racer,
                out LocalPackSample sample)
            ? sample.AverageSpeed
            : racer != null
                ? racer.CurrentSpeed
                : 0f;
    }

    public float GetCohesionAdjustment(RacerMotor racer)
    {
        if (racer == null ||
            !samples.TryGetValue(
                racer,
                out LocalPackSample sample) ||
            sample.NeighborCount <
            minimumNeighborsForPack)
        {
            return 0f;
        }

        float distanceFromLocalCenter =
            sample.AverageDistance -
            racer.UnwrappedTrackDistance;

        return Mathf.Clamp(
            distanceFromLocalCenter /
            cohesionDistance,
            -1f,
            1f) *
            maximumCohesionAdjustment;
    }

    public bool TryReserveAttackLeader(
        NPCRacerController racer,
        float duration)
    {
        if (racer == null ||
            !limitCoordinatedAttacks)
        {
            return true;
        }

        RemoveExpiredAttackReservations();

        float expirationTime =
            Time.time +
            Mathf.Max(
                0.1f,
                duration);

        if (attackLeaderReservations.ContainsKey(
            racer))
        {
            attackLeaderReservations[racer] =
                expirationTime;

            RefreshAttackDebugCounts();
            return true;
        }

        if (Time.time < nextAttackStartTime ||
            attackLeaderReservations.Count >=
            maximumActiveAttackLeaders)
        {
            return false;
        }

        attackFollowerReservations.Remove(
            racer);

        attackLeaderReservations[racer] =
            expirationTime;

        nextAttackStartTime =
            Time.time +
            attackStartCooldownSeconds;

        RefreshAttackDebugCounts();
        return true;
    }

    public bool TryReserveAttackFollower(
        NPCRacerController racer,
        float duration)
    {
        if (racer == null ||
            !limitCoordinatedAttacks)
        {
            return true;
        }

        RemoveExpiredAttackReservations();

        float expirationTime =
            Time.time +
            Mathf.Max(
                0.1f,
                duration);

        if (attackFollowerReservations.ContainsKey(
            racer))
        {
            attackFollowerReservations[racer] =
                expirationTime;

            RefreshAttackDebugCounts();
            return true;
        }

        if (attackLeaderReservations.ContainsKey(
            racer))
        {
            return false;
        }

        if (attackFollowerReservations.Count >=
            maximumActiveAttackFollowers)
        {
            return false;
        }

        attackFollowerReservations[racer] =
            expirationTime;

        RefreshAttackDebugCounts();
        return true;
    }

    public void ReleaseAttackReservation(
        NPCRacerController racer)
    {
        if (racer == null)
        {
            return;
        }

        attackLeaderReservations.Remove(
            racer);

        attackFollowerReservations.Remove(
            racer);

        RefreshAttackDebugCounts();
    }

    private void RefreshSamples()
    {
        samples.Clear();

        IReadOnlyList<RacerMotor> racers =
            RacerRegistry.Racers;

        foreach (RacerMotor racer in racers)
        {
            if (racer == null)
            {
                continue;
            }

            float totalSpeed = 0f;
            float totalDistance = 0f;
            int neighborCount = 0;

            foreach (RacerMotor otherRacer in racers)
            {
                if (otherRacer == null ||
                    otherRacer == racer ||
                    otherRacer.Track != racer.Track)
                {
                    continue;
                }

                float distanceDifference =
                    Mathf.Abs(
                        otherRacer.UnwrappedTrackDistance -
                        racer.UnwrappedTrackDistance);

                if (distanceDifference >
                    localPackRadius)
                {
                    continue;
                }

                totalSpeed +=
                    otherRacer.CurrentSpeed;

                totalDistance +=
                    otherRacer.UnwrappedTrackDistance;

                neighborCount++;
            }

            LocalPackSample sample =
                new LocalPackSample
                {
                    NeighborCount =
                        neighborCount,

                    AverageSpeed =
                        neighborCount > 0
                            ? totalSpeed /
                            neighborCount
                            : racer.CurrentSpeed,

                    AverageDistance =
                        neighborCount > 0
                            ? totalDistance /
                            neighborCount
                            : racer.UnwrappedTrackDistance
                };

            samples[racer] =
                sample;
        }
    }

    private void RefreshStrategicRoles()
    {
        CollectActiveNpcRacers();

        if (npcRacers.Count == 0)
        {
            ResetRoleDebugCounts();
            return;
        }

        npcRacers.Sort(
            distanceComparer);

        FindLargestConnectedPeloton();

        if (mainPelotonRacers.Count == 0)
        {
            ResetRoleDebugCounts();
            return;
        }

        detachedRacers.Clear();

        foreach (NPCRacerController npc in
            npcRacers)
        {
            if (npc == null ||
                mainPelotonRacers.Contains(npc))
            {
                continue;
            }

            if (npc.RaceDistance >
                detectedPelotonFrontDistance +
                detachedRiderMinimumGap)
            {
                detachedRacers.Add(
                    npc);
            }
        }

        detachedRacers.Sort(
            distanceComparer);

        detectedDetachedRiderCount =
            detachedRacers.Count;

        UpdatePlayerBreakawayResponse();
        UpdateCurrentStrategicTargets();

        permittedDetachedRiderCount =
            Mathf.Min(
                currentMaximumDetachedChaseRiders,
                detectedDetachedRiderCount);

        overflowDetachedRiderCount =
            Mathf.Max(
                0,
                detectedDetachedRiderCount -
                permittedDetachedRiderCount);

        detectedPelotonAverageSpeedFraction =
            CalculateMainPelotonAverageSpeedFraction();

        currentOverflowReturnTargetSpeedFraction =
            CalculateOverflowReturnTargetSpeedFraction();

        AssignMainPelotonRoles();
        AssignDetachedRoles();
        AssignStragglerRoles();
    }

    private void CollectActiveNpcRacers()
    {
        npcRacers.Clear();

        foreach (RacerMotor racer in
            RacerRegistry.Racers)
        {
            if (racer == null ||
                !racer.MovementEnabled)
            {
                continue;
            }

            NPCRacerController npc =
                racer.GetComponent<
                    NPCRacerController>();

            if (npc != null)
            {
                npcRacers.Add(
                    npc);
            }
        }
    }

    private void FindLargestConnectedPeloton()
    {
        mainPelotonRacers.Clear();

        int bestStartIndex = 0;
        int bestCount = 0;
        int currentStartIndex = 0;

        for (int index = 0;
            index < npcRacers.Count;
            index++)
        {
            bool startsNewGroup =
                index > 0 &&
                npcRacers[index].RaceDistance -
                npcRacers[index - 1].RaceDistance >
                mainPelotonLinkDistance;

            if (startsNewGroup)
            {
                int currentCount =
                    index -
                    currentStartIndex;

                if (currentCount > bestCount)
                {
                    bestStartIndex =
                        currentStartIndex;

                    bestCount =
                        currentCount;
                }

                currentStartIndex =
                    index;
            }
        }

        int finalCount =
            npcRacers.Count -
            currentStartIndex;

        if (finalCount > bestCount)
        {
            bestStartIndex =
                currentStartIndex;

            bestCount =
                finalCount;
        }

        for (int index = bestStartIndex;
            index < bestStartIndex +
            bestCount;
            index++)
        {
            mainPelotonRacers.Add(
                npcRacers[index]);
        }

        detectedPelotonRiderCount =
            mainPelotonRacers.Count;

        if (detectedPelotonRiderCount == 0)
        {
            detectedPelotonRearDistance = 0f;
            detectedPelotonFrontDistance = 0f;
            return;
        }

        detectedPelotonRearDistance =
            mainPelotonRacers[0].RaceDistance;

        detectedPelotonFrontDistance =
            mainPelotonRacers[
                mainPelotonRacers.Count - 1]
                .RaceDistance;
    }

    private void AssignMainPelotonRoles()
    {
        int firstFrontIndex =
            Mathf.Max(
                0,
                mainPelotonRacers.Count -
                pelotonFrontRiderCount);

        for (int index = 0;
            index < mainPelotonRacers.Count;
            index++)
        {
            NPCRacerController npc =
                mainPelotonRacers[index];

            if (npc == null)
            {
                continue;
            }

            bool isFrontRider =
                index >=
                firstFrontIndex;

            npc.ClearPelotonReturnOverride();

            ApplyPelotonStrategicOrder(
                npc,
                isFrontRider
                ? NPCRacerController
                    .StrategicRole
                    .PelotonFront
                : NPCRacerController
                    .StrategicRole
                    .PelotonBody,
                isFrontRider
                    ? currentPelotonFrontTargetSpeedFraction
                    : currentPelotonBodyTargetSpeedFraction,
                isFrontRider);
        }
    }

    private void AssignDetachedRoles()
    {
        // detachedRacers is ordered from nearest to farthest ahead.
        // Keep the front-most permitted racers in chase formation and force
        // newer overflow riders closest to the peloton into a hard return
        // mode so their local cluster cannot continue pacing itself.
        int firstPermittedIndex =
            Mathf.Max(
                0,
                detachedRacers.Count -
                currentMaximumDetachedChaseRiders);

        for (int index = 0;
            index < detachedRacers.Count;
            index++)
        {
            NPCRacerController npc =
                detachedRacers[index];

            if (npc == null)
            {
                continue;
            }

            bool isPermittedDetachedRider =
                index >=
                firstPermittedIndex;

            if (npc.IsActivelyAttacking)
            {
                npc.ClearPelotonReturnOverride();

                ApplyPelotonStrategicOrder(
                    npc,
                    NPCRacerController
                    .StrategicRole
                    .BreakawayLeader,
                currentChaseGroupTargetSpeedFraction,
                true);

                continue;
            }

            if (npc.IsActivelyFollowingAttack ||
                isPermittedDetachedRider)
            {
                npc.ClearPelotonReturnOverride();

                ApplyPelotonStrategicOrder(
                    npc,
                    NPCRacerController
                        .StrategicRole
                        .ChaseGroup,
                    currentChaseGroupTargetSpeedFraction,
                    false);

                continue;
            }

            npc.CancelCoordinatedAttack();

            ApplyPelotonStrategicOrder(
                npc,
                NPCRacerController
                   .StrategicRole
                    .PelotonBody,
                currentOverflowReturnTargetSpeedFraction,
                false);

            npc.SetPelotonReturnOverride(
                currentOverflowReturnTargetSpeedFraction,
                refreshInterval * 2.5f);
        }
    }

    private void AssignStragglerRoles()
    {
        foreach (NPCRacerController npc in
            npcRacers)
        {
            if (npc == null ||
                mainPelotonRacers.Contains(npc) ||
                detachedRacers.Contains(npc))
            {
                continue;
            }

            npc.ClearPelotonReturnOverride();

            ApplyPelotonStrategicOrder(
                npc,
                NPCRacerController
                    .StrategicRole
                    .PelotonBody,
                currentPelotonBodyTargetSpeedFraction,
                 false);
        }
    }

    private void FindPlayerMotorIfMissing()
    {
        if (playerMotor != null &&
            playerMotor.MovementEnabled)
        {
            return;
        }

        playerMotor = null;

        foreach (RacerMotor racer in
            RacerRegistry.Racers)
        {
            if (racer == null ||
                !racer.MovementEnabled ||
                racer.GetComponent<
                    NPCRacerController>() != null)
            {
                continue;
            }

            playerMotor = racer;
            return;
        }
    }

    private void UpdatePlayerBreakawayResponse()
    {
        detectedPlayerGapAhead = 0f;

        float desiredResponse = 0f;

        if (respondToPlayerBreakaway &&
            playerMotor != null &&
            playerMotor.MovementEnabled)
        {
            detectedPlayerGapAhead =
                Mathf.Max(
                    0f,
                    playerMotor.UnwrappedTrackDistance -
                    detectedPelotonFrontDistance);

            float fullResponseGap =
                Mathf.Max(
                    playerBreakawayResponseStartsAtGap +
                    0.1f,
                    playerBreakawayFullResponseGap);

            desiredResponse =
                Mathf.InverseLerp(
                    playerBreakawayResponseStartsAtGap,
                    fullResponseGap,
                    detectedPlayerGapAhead);
        }

        currentPlayerBreakawayResponse =
            Mathf.MoveTowards(
                currentPlayerBreakawayResponse,
                desiredResponse,
                pursuitResponseChangePerSecond *
                refreshInterval);
    }

    private void UpdateCurrentStrategicTargets()
    {
        currentPelotonBodyTargetSpeedFraction =
            Mathf.Clamp01(
                pelotonBodySpeedFraction +
                maximumPelotonBodyPursuitBoost *
                currentPlayerBreakawayResponse);

        currentPelotonFrontTargetSpeedFraction =
            Mathf.Clamp01(
                pelotonFrontSpeedFraction +
                maximumPelotonFrontPursuitBoost *
                currentPlayerBreakawayResponse);

        currentChaseGroupTargetSpeedFraction =
            Mathf.Clamp01(
                chaseGroupSpeedFraction +
                maximumDetachedChasePursuitBoost *
                currentPlayerBreakawayResponse);

        currentMaximumDetachedChaseRiders =
            maximumDetachedChaseRiders +
            Mathf.RoundToInt(
                additionalChaseRidersAtFullResponse *
                currentPlayerBreakawayResponse);
    }

    private float CalculateMainPelotonAverageSpeedFraction()
    {
        float totalSpeedFraction = 0f;
        int validRiderCount = 0;

        foreach (NPCRacerController npc in
            mainPelotonRacers)
        {
            if (npc == null ||
                npc.Motor == null ||
                npc.Motor.MaximumSpeed <= 0f)
            {
                continue;
            }

            totalSpeedFraction +=
                npc.Motor.CurrentSpeed /
                npc.Motor.MaximumSpeed;

            validRiderCount++;
        }

        return validRiderCount > 0
            ? totalSpeedFraction /
                validRiderCount
            : pelotonBodySpeedFraction;
    }

    private float CalculateOverflowReturnTargetSpeedFraction()
    {
        float dynamicReturnTarget =
            detectedPelotonAverageSpeedFraction -
            overflowReturnSpeedMargin;

        return Mathf.Clamp(
            Mathf.Min(
                overflowReturnSpeedFraction,
                dynamicReturnTarget),
            minimumOverflowReturnSpeedFraction,
            1f);
    }

    private void RemoveExpiredAttackReservations()
    {
        RemoveExpiredReservationsFrom(
            attackLeaderReservations);

        RemoveExpiredReservationsFrom(
            attackFollowerReservations);

        RefreshAttackDebugCounts();
    }

    private void RemoveExpiredReservationsFrom(
        Dictionary<NPCRacerController, float>
            reservations)
    {
        List<NPCRacerController> expiredRacers =
            null;

        foreach (KeyValuePair<
            NPCRacerController,
            float> reservation in
            reservations)
        {
            if (reservation.Key == null ||
                Time.time >=
                reservation.Value)
            {
                if (expiredRacers == null)
                {
                    expiredRacers =
                        new List<
                            NPCRacerController>();
                }

                expiredRacers.Add(
                    reservation.Key);
            }
        }

        if (expiredRacers == null)
        {
            return;
        }

        foreach (NPCRacerController racer in
            expiredRacers)
        {
            reservations.Remove(
                racer);
        }
    }

    private void RefreshAttackDebugCounts()
    {
        activeAttackLeaderCount =
            attackLeaderReservations.Count;

        activeAttackFollowerCount =
            attackFollowerReservations.Count;
    }

    private void ResetRoleDebugCounts()
    {
        detectedPelotonRearDistance = 0f;
        detectedPelotonFrontDistance = 0f;
        detectedPelotonRiderCount = 0;
        detectedDetachedRiderCount = 0;
        permittedDetachedRiderCount = 0;
        overflowDetachedRiderCount = 0;
        detectedPelotonAverageSpeedFraction = 0f;
        currentOverflowReturnTargetSpeedFraction = 0f;
        detectedPlayerGapAhead = 0f;
        currentMaximumDetachedChaseRiders = 0;
        currentPelotonBodyTargetSpeedFraction = 0f;
        currentPelotonFrontTargetSpeedFraction = 0f;
        currentChaseGroupTargetSpeedFraction = 0f;
    }

    private void ApplyPelotonStrategicOrder(
    NPCRacerController npc,
    NPCRacerController.StrategicRole role,
    float targetSpeedFraction,
    bool allowNormalPassing)
    {
        if (!assignStrategicRoles ||
            npc == null)
        {
            return;
        }

        npc.SetStrategicOrder(
            role,
            targetSpeedFraction,
            allowNormalPassing);
    }
}
