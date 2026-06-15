using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerPelotonDirectionalPush : MonoBehaviour
{
    [Header("Required References")]
    [SerializeField] private RacerMotor motor;
    [SerializeField] private RacerEnergy energy;

    [Header("ATP Costs")]
    [SerializeField, Min(0f)] private float sidewaysPushATPCost = 3f;
    [SerializeField, Min(0f)] private float forwardWedgeATPCost = 7f;

    [Header("Forward Surge")]
    [Tooltip("Minimum throttle applied briefly after a directional push.")]
    [SerializeField, Range(0f, 1f)] private float surgeThrottleFloor = 0.95f;

    [Tooltip("Duration of the brief surge after a directional push.")]
    [SerializeField, Min(0.05f)] private float surgeDuration = 0.45f;

    [Header("Sideways Push Shape")]
    [Tooltip("How far ahead a sideways push affects riders.")]
    [SerializeField, Min(0.1f)] private float sidewaysForwardRange = 5f;

    [Tooltip("How wide the sideways push area is.")]
    [SerializeField, Min(0.1f)] private float sidewaysWidth = 4f;

    [Header("Forward Triangle Shape")]
    [Tooltip("How far ahead the triangular wedge extends.")]
    [SerializeField, Min(0.1f)] private float wedgeLength = 8f;

    [Tooltip("Width of the wedge at its widest point.")]
    [SerializeField, Min(0.1f)] private float wedgeWidth = 8f;

    [Header("Timing")]
    [Tooltip("Minimum delay between push actions.")]
    [SerializeField, Min(0f)] private float pushCooldown = 0.25f;

    [Header("Debug")]
    [SerializeField] private string lastPush = "None";

    private float nextPushTime;

    private void Awake()
    {
        if (motor == null) motor = GetComponent<RacerMotor>();
        if (energy == null) energy = GetComponent<RacerEnergy>();
    }

    private void Update()
    {
        if (motor == null ||
            energy == null ||
            !motor.MovementEnabled ||
            Keyboard.current == null ||
            !Keyboard.current.qKey.isPressed ||
            Time.time < nextPushTime)
        {
            return;
        }

        if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
        {
            TrySidewaysPush(1);
            return;
        }

        if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
        {
            TrySidewaysPush(-1);
            return;
        }

        if (Keyboard.current.upArrowKey.wasPressedThisFrame)
        {
            TryForwardWedgePush();
        }
    }

    private void TrySidewaysPush(int direction)
    {
        if (!energy.TrySpendATP(sidewaysPushATPCost))
        {
            return;
        }

        PushNearbyNPCsSideways(direction);

        // Move the player into the opening after NPCs begin yielding.
        motor.ChangeLane(direction);
        motor.StartTemporaryThrottleBoost(surgeThrottleFloor, surgeDuration);

        nextPushTime = Time.time + pushCooldown;
        lastPush = direction > 0 ? "Right" : "Left";
    }

    private void TryForwardWedgePush()
    {
        if (!energy.TrySpendATP(forwardWedgeATPCost))
        {
            return;
        }

        PushNPCsOutwardFromWedge();
        motor.StartTemporaryThrottleBoost(1f, surgeDuration * 1.5f);

        nextPushTime = Time.time + pushCooldown;
        lastPush = "Forward Wedge";
    }

    private void PushNearbyNPCsSideways(int direction)
    {
        if (motor.Track == null || motor.Track.Length <= 0f)
        {
            return;
        }

        foreach (RacerMotor otherRacer in RacerRegistry.Racers)
        {
            if (!IsNPC(otherRacer))
            {
                continue;
            }

            float forwardGap = GetForwardGap(otherRacer);

            if (forwardGap <= 0f || forwardGap > sidewaysForwardRange)
            {
                continue;
            }

            float sidewaysGap =
                otherRacer.CurrentSidewaysOffset -
                motor.CurrentSidewaysOffset;

            bool isInsidePushStrip =
                direction > 0
                    ? sidewaysGap >= -0.5f && sidewaysGap <= sidewaysWidth
                    : sidewaysGap <= 0.5f && sidewaysGap >= -sidewaysWidth;

            if (isInsidePushStrip)
            {
                if (otherRacer.TryGetComponent(out NPCPelotonYield npcYield))
                {
                    npcYield.BeginYield(direction);
                }
                else
                {
                    otherRacer.ForceLaneShift(direction);
                }
            }
        }
    }

    private void PushNPCsOutwardFromWedge()
    {
        if (motor.Track == null || motor.Track.Length <= 0f)
        {
            return;
        }

        foreach (RacerMotor otherRacer in RacerRegistry.Racers)
        {
            if (!IsNPC(otherRacer))
            {
                continue;
            }

            float forwardGap = GetForwardGap(otherRacer);

            if (forwardGap <= 0f || forwardGap > wedgeLength)
            {
                continue;
            }

            float sidewaysGap =
                otherRacer.CurrentSidewaysOffset -
                motor.CurrentSidewaysOffset;

            float widthAtDistance =
                Mathf.Lerp(
                    motor.Track.LaneSpacing * 0.5f,
                    wedgeWidth * 0.5f,
                    forwardGap / wedgeLength);

            if (Mathf.Abs(sidewaysGap) > widthAtDistance)
            {
                continue;
            }

            int outwardDirection;

            if (Mathf.Abs(sidewaysGap) < 0.1f)
            {
                outwardDirection = Random.value < 0.5f ? -1 : 1;
            }
            else
            {
                outwardDirection = sidewaysGap < 0f ? -1 : 1;
            }

            if (otherRacer.TryGetComponent(out NPCPelotonYield npcYield))
            {
                npcYield.BeginYield(outwardDirection);
            }
            else
            {
                otherRacer.ForceLaneShift(outwardDirection);
            }
        }
    }

    private bool IsNPC(RacerMotor otherRacer)
    {
        return otherRacer != null &&
            otherRacer != motor &&
            otherRacer.GetComponent<NPCRacerController>() != null;
    }

    private float GetForwardGap(RacerMotor otherRacer)
    {
        return Mathf.Repeat(
            otherRacer.DistanceAlongTrack -
            motor.DistanceAlongTrack,
            motor.Track.Length);
    }
}