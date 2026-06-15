using UnityEngine;

public class RacerAwareness : MonoBehaviour
{
    [Header("Required Reference")]
    [SerializeField] private RacerMotor motor;

    [Header("Collision")]
    [Tooltip(
        "Minimum distance that must remain between racers " +
        "when they occupy the same lane.")]
    [SerializeField, Min(0.1f)]
    private float minimumFollowingDistance = 2.5f;

    [Tooltip(
        "Distance at which the following racer begins matching " +
        "the speed of the racer ahead.")]
    [SerializeField, Min(0.1f)]
    private float speedMatchingDistance = 4f;

    [Header("Drafting")]
    [Tooltip(
        "Maximum distance at which a racer can draft " +
        "behind another racer.")]
    [SerializeField, Min(0.1f)]
    private float draftingDistance = 8f;

    [Header("Lane Change Safety")]
    [Tooltip(
        "Required open space in front of and behind the racer " +
        "before entering another lane.")]
    [SerializeField, Min(0.1f)]
    private float laneChangeClearance = 3f;

    [Tooltip(
    "Lane-change clearance multiplier used while the player " +
    "is swimming through the peloton.")]
    [SerializeField, Range(0.1f, 1f)]
    private float swimmingClearanceMultiplier = 0.45f;

    [Tooltip(
        "How much of a lane width counts as occupying the same lane.")]
    [SerializeField, Range(0.1f, 1f)]
    private float lateralOverlapFraction = 0.7f;

    private RacerMotor racerAhead;
    private float distanceToRacerAhead = float.PositiveInfinity;

    public RacerMotor RacerAhead => racerAhead;
    public float DistanceToRacerAhead => distanceToRacerAhead;
    public float MinimumFollowingDistance => minimumFollowingDistance;
    public float SpeedMatchingDistance => speedMatchingDistance;

    public bool HasRacerAhead => racerAhead != null;

    public bool IsBlocked =>
        HasRacerAhead &&
        distanceToRacerAhead <= minimumFollowingDistance;

    public bool IsDrafting =>
        HasRacerAhead &&
        distanceToRacerAhead > 0f &&
        distanceToRacerAhead <= draftingDistance;

    private void Awake()
    {
        if (motor == null)
        {
            motor = GetComponent<RacerMotor>();
        }
    }

    private void Update()
    {
        Refresh();
    }

    public void Refresh()
    {
        FindClosestRacerAhead();
    }

    private void FindClosestRacerAhead()
    {
        racerAhead = null;
        distanceToRacerAhead = float.PositiveInfinity;

        if (motor == null ||
            motor.Track == null)
        {
            return;
        }

        foreach (RacerMotor otherRacer in RacerRegistry.Racers)
        {
            if (otherRacer == null ||
                otherRacer == motor ||
                otherRacer.Track != motor.Track)
            {
                continue;
            }

            if (!SharesLaneSpace(otherRacer))
            {
                continue;
            }
            float forwardGap =
                GetForwardPhysicalGap(
                    otherRacer);

            // Racers in the same grid row may have the same track distance.
            // They are beside one another, not ahead of one another.
            if (forwardGap <= 0.05f)
            {
                continue;
            }

            float maximumRelevantDistance =
                Mathf.Max(
                    draftingDistance,
                    speedMatchingDistance);

            if (forwardGap >
                maximumRelevantDistance)
            {
                continue;
            }

            if (forwardGap < distanceToRacerAhead)
            {
                racerAhead = otherRacer;
                distanceToRacerAhead = forwardGap;
            }
        }
    }

    public bool TryFindNearestRacerAhead(
        float maximumForwardDistance,
        int maximumLaneDifference,
        out RacerMotor nearestRacer,
        out float forwardGap)
    {
        nearestRacer = null;
        forwardGap =
            float.PositiveInfinity;

        if (motor == null ||
            motor.Track == null ||
            maximumForwardDistance <= 0f)
        {
            return false;
        }

        int safeMaximumLaneDifference =
            Mathf.Max(
                0,
                maximumLaneDifference);

        foreach (RacerMotor otherRacer in
            RacerRegistry.Racers)
        {
            if (otherRacer == null ||
                otherRacer == motor ||
                otherRacer.Track != motor.Track ||
                !otherRacer.MovementEnabled)
            {
                continue;
            }

            int laneDifference =
                Mathf.Abs(
                    otherRacer.CurrentTargetLane -
                    motor.CurrentTargetLane);

            if (laneDifference >
                safeMaximumLaneDifference)
            {
                continue;
            }

            // Use unwrapped race distance for group formation. This avoids
            // treating a lapped rider as a nearby wheel simply because both
            // riders occupy similar physical positions on the loop.
            float candidateForwardGap =
                otherRacer.UnwrappedTrackDistance -
                motor.UnwrappedTrackDistance;

            if (candidateForwardGap <= 0.05f ||
                candidateForwardGap >
                maximumForwardDistance ||
                candidateForwardGap >=
                forwardGap)
            {
                continue;
            }

            nearestRacer = otherRacer;
            forwardGap =
                candidateForwardGap;
        }

        return nearestRacer != null;
    }

    public bool CanEnterLane(int desiredLane)
    {
        if (motor == null ||
            motor.Track == null)
        {
            return false;
        }

        desiredLane =
            motor.Track.ClampLane(
                desiredLane);

        float desiredOffset = motor.Track.GetLaneOffset(desiredLane) + motor.LaneBias;

        float lateralClearance =
            motor.Track.LaneSpacing *
            lateralOverlapFraction;

        foreach (RacerMotor otherRacer in RacerRegistry.Racers)
        {
            if (otherRacer == null ||
                otherRacer == motor ||
                otherRacer.Track != motor.Track)
            {
                continue;
            }

            bool occupiesDestinationLane =
                otherRacer.CurrentTargetLane ==
                desiredLane ||
                Mathf.Abs(
                    otherRacer.CurrentSidewaysOffset -
                    desiredOffset) <
                lateralClearance;

            if (!occupiesDestinationLane)
            {
                continue;
            }

            float distanceDifference = GetShortestPhysicalGap(otherRacer);

            float effectiveLaneChangeClearance =
    motor.IsSwimming
        ? laneChangeClearance * swimmingClearanceMultiplier
        : laneChangeClearance;

            if (Mathf.Abs(distanceDifference) <
                effectiveLaneChangeClearance)
            {
                return false;
            }
        }

        return true;
    }

    private bool SharesLaneSpace(
        RacerMotor otherRacer)
    {
        float lateralClearance =
            motor.Track.LaneSpacing *
            lateralOverlapFraction;

        return
            Mathf.Abs(
                otherRacer.CurrentSidewaysOffset -
                motor.CurrentSidewaysOffset) <
            lateralClearance;
    }

    private float GetForwardPhysicalGap(
    RacerMotor otherRacer)
    {
        if (motor == null ||
            motor.Track == null ||
            motor.Track.Length <= 0f)
        {
            return
                float.PositiveInfinity;
        }

        return Mathf.Repeat(
            otherRacer.DistanceAlongTrack -
            motor.DistanceAlongTrack,
            motor.Track.Length);
    }

    private float GetShortestPhysicalGap(
        RacerMotor otherRacer)
    {
        float forwardGap =
            GetForwardPhysicalGap(
                otherRacer);

        float trackLength =
            motor.Track.Length;

        if (forwardGap >
            trackLength * 0.5f)
        {
            return
                forwardGap -
                trackLength;
        }

        return
            forwardGap;
    }
}