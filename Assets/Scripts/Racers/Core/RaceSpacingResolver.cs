using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central spacing resolver for spline/lane racers.
///
/// RacerMotor still decides each rider's speed and target lane.
/// This resolver is the only script that advances racers along the track
/// when external spacing is enabled. It prevents riders in the same target
/// lane from ending the frame inside each other.
///
/// This is intentionally kinematic, not Rigidbody physics.
/// </summary>
public class RaceSpacingResolver : MonoBehaviour
{
    [Header("Resolver")]
    [SerializeField] private bool resolverEnabled = true;

    [Tooltip("Minimum front-to-back gap between NPCs in the same target lane.")]
    [SerializeField, Min(0.1f)] private float npcMinimumGap = 1.25f;

    [Tooltip("Minimum front-to-back gap when either rider is the player.")]
    [SerializeField, Min(0.1f)] private float playerMinimumGap = 1.75f;

    [Tooltip("If true, only racers that are aiming for the same target lane block each other.")]
    [SerializeField] private bool resolveByTargetLane = true;

    [Tooltip("If true, the resolver automatically enables external spacing on all active RacerMotor objects.")]
    [SerializeField] private bool autoEnableMotors = true;

    [Header("Debug")]
    [SerializeField] private int resolvedRacerCount;
    [SerializeField] private int blockedThisFrame;
    [SerializeField] private string mostBlockedRacerName = "None";

    private readonly Dictionary<TrackPath, Dictionary<int, List<RacerMotor>>> racersByTrackAndLane = new();

    private void OnEnable()
    {
        SetMotorsExternalSpacing(true);
    }

    private void OnDisable()
    {
        SetMotorsExternalSpacing(false);
    }

    private void LateUpdate()
    {
        if (!resolverEnabled)
        {
            return;
        }

        if (autoEnableMotors)
        {
            SetMotorsExternalSpacing(true);
        }

        resolvedRacerCount = 0;
        blockedThisFrame = 0;
        mostBlockedRacerName = "None";

        BuildLaneBuckets();
        ResolveLaneBuckets();
    }

    private void SetMotorsExternalSpacing(bool enabled)
    {
        IReadOnlyList<RacerMotor> racers = RacerRegistry.Racers;

        for (int i = 0; i < racers.Count; i++)
        {
            RacerMotor motor = racers[i];

            if (motor == null)
            {
                continue;
            }

            motor.SetUseExternalSpacingResolver(enabled && resolverEnabled);
        }
    }

    private void BuildLaneBuckets()
    {
        racersByTrackAndLane.Clear();

        IReadOnlyList<RacerMotor> racers = RacerRegistry.Racers;

        for (int i = 0; i < racers.Count; i++)
        {
            RacerMotor motor = racers[i];

            if (motor == null ||
                !motor.MovementEnabled ||
                motor.Track == null ||
                motor.Track.Length <= 0f)
            {
                continue;
            }

            TrackPath track = motor.Track;
            int laneKey = resolveByTargetLane
                ? motor.CurrentTargetLane
                : Mathf.RoundToInt(motor.CurrentSidewaysOffset / Mathf.Max(0.01f, track.LaneSpacing));

            if (!racersByTrackAndLane.TryGetValue(track, out Dictionary<int, List<RacerMotor>> laneBuckets))
            {
                laneBuckets = new Dictionary<int, List<RacerMotor>>();
                racersByTrackAndLane.Add(track, laneBuckets);
            }

            if (!laneBuckets.TryGetValue(laneKey, out List<RacerMotor> laneRacers))
            {
                laneRacers = new List<RacerMotor>();
                laneBuckets.Add(laneKey, laneRacers);
            }

            laneRacers.Add(motor);
        }
    }

    private void ResolveLaneBuckets()
    {
        foreach (KeyValuePair<TrackPath, Dictionary<int, List<RacerMotor>>> trackEntry in racersByTrackAndLane)
        {
            foreach (KeyValuePair<int, List<RacerMotor>> laneEntry in trackEntry.Value)
            {
                ResolveSingleLane(laneEntry.Value);
            }
        }
    }

    private void ResolveSingleLane(List<RacerMotor> laneRacers)
    {
        if (laneRacers == null ||
            laneRacers.Count == 0)
        {
            return;
        }

        // Front to rear by unwrapped race distance.
        laneRacers.Sort(
            (a, b) => b.UnwrappedTrackDistance.CompareTo(a.UnwrappedTrackDistance));

        float leaderResolvedEndDistance = float.PositiveInfinity;
        RacerMotor leader = null;

        for (int i = 0; i < laneRacers.Count; i++)
        {
            RacerMotor motor = laneRacers[i];

            if (motor == null)
            {
                continue;
            }

            float startDistance = motor.UnwrappedTrackDistance;
            float desiredMove = Mathf.Max(0f, motor.CurrentSpeed * Time.deltaTime);
            float desiredEndDistance = startDistance + desiredMove;
            float resolvedEndDistance = desiredEndDistance;

            if (leader != null)
            {
                float requiredGap = GetRequiredGap(motor, leader);
                float maximumAllowedEndDistance = leaderResolvedEndDistance - requiredGap;

                if (resolvedEndDistance > maximumAllowedEndDistance)
                {
                    resolvedEndDistance = maximumAllowedEndDistance;
                    blockedThisFrame++;
                    mostBlockedRacerName = motor.name;
                }
            }

            float resolvedMove = Mathf.Max(0f, resolvedEndDistance - startDistance);

            motor.ApplyResolvedMovement(resolvedMove);

            // If we held this rider back, drop its modelled speed to the speed it
            // was actually allowed to move. Otherwise it keeps pushing faster than
            // it can travel and lunges forward whenever the gap opens (the rear bob).
            bool wasHeldBack =
                leader != null &&
                resolvedEndDistance < desiredEndDistance - 0.0001f &&
                Time.deltaTime > 0f;

            if (wasHeldBack)
            {
                // Match the rider ahead's pace, not the distance we were allowed
                // this frame. The latter is zero when a rider is wedged in (e.g.
                // just pushed), which drops it to a dead stop and stalls everyone
                // behind it.
                motor.LimitSpeedTo(leader.CurrentSpeed);
            }

            leaderResolvedEndDistance = startDistance + resolvedMove;
            leader = motor;
            resolvedRacerCount++;
        }
    }

    private float GetRequiredGap(RacerMotor follower, RacerMotor leader)
    {
        bool followerIsPlayer = follower != null && follower.GetComponent<NPCRacerController>() == null;
        bool leaderIsPlayer = leader != null && leader.GetComponent<NPCRacerController>() == null;

        return followerIsPlayer || leaderIsPlayer
            ? playerMinimumGap
            : npcMinimumGap;
    }

    
}
