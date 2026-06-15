using UnityEngine;

/// <summary>
/// Player-only soft drafting lock.
/// Attach this to the player racer, not to NPC racers.
/// It does not teleport the player. It gently matches speed to the rider ahead
/// so the player can sit on a wheel through hills and small accordion changes.
/// </summary>
public class PlayerWheelLock : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RacerMotor motor;
    [SerializeField] private RacerAwareness awareness;

    [Header("Lock Distances")]
    [Tooltip("Player can lock to a wheel when the gap is this close or closer.")]
    [SerializeField, Min(0.1f)] private float enterLockGap = 5f;

    [Tooltip("Target distance to hold behind the locked wheel.")]
    [SerializeField, Min(0.1f)] private float idealWheelGap = 2.5f;

    [Tooltip("Wheel lock breaks when the gap grows beyond this distance.")]
    [SerializeField, Min(0.1f)] private float breakLockGap = 8f;

    [Tooltip("Allowed target-lane difference between the player and locked wheel.")]
    [SerializeField, Min(0)] private int maximumLaneDifference = 0;

    [Header("Speed Assist")]
    [Tooltip("How quickly the player speed is nudged toward the locked wheel speed.")]
    [SerializeField, Min(0f)] private float speedMatchAcceleration = 8f;

    [Tooltip("Extra speed added per meter when the player is behind the ideal gap.")]
    [SerializeField, Min(0f)] private float catchUpCorrectionPerMeter = 0.35f;

    [Tooltip("Maximum extra speed added while reconnecting to the wheel.")]
    [SerializeField, Min(0f)] private float maximumCatchUpBonus = 1.5f;

    [Tooltip("Speed reduction per meter when the player is too close to the wheel.")]
    [SerializeField, Min(0f)] private float tooCloseSlowdownPerMeter = 0.45f;

    [Tooltip("Maximum speed reduction when the player is too close to the wheel.")]
    [SerializeField, Min(0f)] private float maximumTooCloseSlowdown = 1.5f;

    [Header("Intentional Release")]
    [Tooltip("If watts fall below this, the player is considered to be letting the wheel go.")]
    [SerializeField, Range(0f, 100f)] private float minimumWattsToHoldWheel = 5f;

    [Header("Debug")]
    [SerializeField] private bool isWheelLocked;
    [SerializeField] private string lockedWheelName = "None";
    [SerializeField] private float currentWheelGap;
    [SerializeField] private float wheelTargetSpeed;

    private RacerMotor lockedWheel;

    public bool IsWheelLocked => isWheelLocked && lockedWheel != null;
    public RacerMotor LockedWheel => lockedWheel;
    public float CurrentWheelGap => currentWheelGap;

    private void Reset()
    {
        motor = GetComponent<RacerMotor>();
        awareness = GetComponent<RacerAwareness>();
    }

    private void Awake()
    {
        if (motor == null)
        {
            motor = GetComponent<RacerMotor>();
        }

        if (awareness == null)
        {
            awareness = GetComponent<RacerAwareness>();
        }
    }

    private void LateUpdate()
    {
        if (motor == null ||
            awareness == null ||
            !motor.MovementEnabled)
        {
            ClearLock();
            return;
        }

        if (IsWheelLocked)
        {
            if (!IsCurrentLockValid())
            {
                ClearLock();
                return;
            }

            ApplyWheelAssist();
            return;
        }

        TryAcquireWheel();
    }

    private void TryAcquireWheel()
    {
        if (!awareness.TryFindNearestRacerAhead(
            enterLockGap,
            maximumLaneDifference,
            out RacerMotor candidate,
            out float gap))
        {
            ClearLock();
            return;
        }

        if (candidate == null ||
            candidate == motor ||
            candidate.Track != motor.Track)
        {
            ClearLock();
            return;
        }

        lockedWheel = candidate;
        isWheelLocked = true;
        currentWheelGap = gap;
        lockedWheelName = lockedWheel.name;

        ApplyWheelAssist();
    }

    private bool IsCurrentLockValid()
    {
        if (lockedWheel == null ||
            lockedWheel == motor ||
            lockedWheel.Track != motor.Track ||
            !lockedWheel.MovementEnabled)
        {
            return false;
        }

        if (motor.WattsPercent < minimumWattsToHoldWheel)
        {
            return false;
        }

        int laneDifference = Mathf.Abs(
            lockedWheel.CurrentTargetLane -
            motor.CurrentTargetLane);

        if (laneDifference > maximumLaneDifference)
        {
            return false;
        }

        currentWheelGap = GetForwardGapToLockedWheel();

        if (currentWheelGap <= 0.05f ||
            currentWheelGap > breakLockGap)
        {
            return false;
        }

        lockedWheelName = lockedWheel.name;
        return true;
    }

    private void ApplyWheelAssist()
    {
        if (lockedWheel == null)
        {
            return;
        }

        float leaderSpeed = lockedWheel.CurrentSpeed;
        float gapError = currentWheelGap - idealWheelGap;
        float correction = 0f;

        if (gapError > 0f)
        {
            correction = Mathf.Clamp(
                gapError * catchUpCorrectionPerMeter,
                0f,
                maximumCatchUpBonus);
        }
        else if (gapError < 0f)
        {
            correction = -Mathf.Clamp(
                -gapError * tooCloseSlowdownPerMeter,
                0f,
                maximumTooCloseSlowdown);
        }

        wheelTargetSpeed = Mathf.Clamp(
            leaderSpeed + correction,
            0f,
            motor.MaximumSpeed * 1.15f);

        motor.NudgeSpeedToward(
            wheelTargetSpeed,
            speedMatchAcceleration);
    }

    private float GetForwardGapToLockedWheel()
    {
        if (motor == null ||
            lockedWheel == null ||
            motor.Track == null ||
            motor.Track.Length <= 0f)
        {
            return float.PositiveInfinity;
        }

        return Mathf.Repeat(
            lockedWheel.DistanceAlongTrack - motor.DistanceAlongTrack,
            motor.Track.Length);
    }

    private void ClearLock()
    {
        lockedWheel = null;
        isWheelLocked = false;
        lockedWheelName = "None";
        currentWheelGap = float.PositiveInfinity;
        wheelTargetSpeed = 0f;
    }
}
