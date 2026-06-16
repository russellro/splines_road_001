using UnityEngine;

public class NPCRacerController : MonoBehaviour
{
    private enum TacticState
    {
        Waiting,
        Cruising,
        Drafting,
        Passing,
        ReturningToPeloton
    }

    public enum StrategicRole
    {
        PelotonBody,
        PelotonFront,

        // Legacy values are kept for compile compatibility with older scripts.
        // The clean controller does not run special breakaway/chase behavior.
        BreakawayLeader,
        BreakawayFollower,
        ChaseGroup
    }

    [Header("Required References")]
    [SerializeField] private RacerMotor motor;
    [SerializeField] private RacerAwareness awareness;
    [SerializeField] private RacerProgress progress;

    [Header("Race Strategy")]
    [SerializeField] private StrategicRole strategicRole = StrategicRole.PelotonBody;

    [Tooltip("Speed fraction assigned by SimplePelotonRoleAssigner.")]
    [SerializeField, Range(0f, 1f)] private float strategicTargetSpeedFraction = 0.62f;

    [Tooltip("Whether this rider may make ordinary passing decisions.")]
    [SerializeField] private bool allowNormalPassing;

    [Header("Simple Behavior")]
    [Tooltip("When drafting, target speed is multiplied by this value. Keep near 1 for a cohesive pack.")]
    [SerializeField, Range(0.5f, 1.05f)] private float draftingSpeedMultiplier = 0.98f;

    [Tooltip("Temporary speed added while making a pass.")]
    [SerializeField, Range(0f, 0.25f)] private float passingSpeedBonus = 0.07f;

    [Tooltip("NPC immediately tries to pass if the rider ahead is slower by at least this fraction of maximum speed.")]
    [SerializeField, Range(0f, 0.5f)] private float urgentPassSpeedDifferenceFraction = 0.08f;

    [Tooltip("NPC considers passing when a rider is within this distance.")]
    [SerializeField, Min(0.1f)] private float passDecisionDistance = 7f;

    [Tooltip("Probability of attempting an ordinary pass during each AI decision.")]
    [SerializeField, Range(0f, 1f)] private float passChance = 0.25f;

    [Tooltip("How long the NPC remains committed to a pass.")]
    [SerializeField, Min(0.1f)] private float passingDuration = 2.0f;

    [Tooltip("Minimum time between lane changes.")]
    [SerializeField, Min(0.1f)] private float laneChangeCooldown = 1.2f;

    [Tooltip("Chance that an NPC returns toward the middle lanes after cruising.")]
    [SerializeField, Range(0f, 1f)] private float returnTowardCenterChance = 0.20f;

    [Header("Peloton Return Override")]
    [Tooltip("When active, this rider deliberately rides slower so the main peloton can reabsorb it.")]
    [SerializeField] private bool pelotonReturnOverrideActive;

    [SerializeField, Range(0f, 1f)] private float pelotonReturnTargetSpeedFraction = 0.35f;
    [SerializeField] private float pelotonReturnOverrideUntil;

    [Header("Decision Timing")]
    [SerializeField, Min(0.05f)] private float decisionInterval = 0.4f;
    [SerializeField, Min(0.01f)] private float speedTolerance = 0.15f;

    [Tooltip("How gently the NPC corrects toward its target speed. Smaller = sharper/more on-off. ~2 m/s eases in smoothly.")]
    [SerializeField, Min(0.1f)] private float speedCorrectionRange = 2f;

    [Tooltip("Damping that resists rapid speed changes — this is what stops the pulsing. Start at 0.25; raise it if it still hunts.")]
    [SerializeField, Min(0f)] private float speedDamping = 0.25f;

    [Header("Debug")]
    [SerializeField] private TacticState currentState = TacticState.Waiting;
    [SerializeField] private string debugRole = "PelotonBody";
    [SerializeField, Range(0f, 1f)] private float debugTargetSpeedFraction;
    [SerializeField] private bool debugReturningToPeloton;
    [SerializeField] private bool debugAllowNormalPassing;

    private float nextDecisionTime;
    private float nextLaneChangeTime;
    private float passingUntilTime;

    private float previousSpeed;

    public RacerMotor Motor => motor;

    public float RaceDistance =>
        motor != null
            ? motor.UnwrappedTrackDistance
            : 0f;

    public StrategicRole CurrentStrategicRole => strategicRole;

    public bool IsReturningToPeloton =>
        pelotonReturnOverrideActive &&
        Time.time < pelotonReturnOverrideUntil;

    // Compatibility properties for old/disabled managers.
    public float ATPFraction => 1f;
    public bool IsEligibleForPelotonCirculation =>
        strategicRole == StrategicRole.PelotonBody ||
        strategicRole == StrategicRole.PelotonFront;
    public bool IsActivelyAttacking => false;
    public bool IsActivelyFollowingAttack => false;
    public bool CanStartAttack => false;
    public bool CanFollowAttack => false;

    private bool CanMakeUrgentPass()
    {
        return false;
    }

    private void Awake()
    {
        if (motor == null) motor = GetComponent<RacerMotor>();
        if (awareness == null) awareness = GetComponent<RacerAwareness>();
        if (progress == null) progress = GetComponent<RacerProgress>();
    }

    private void Update()
    {
        UpdateDebugFields();

        if (motor == null)
        {
            return;
        }

        if (!motor.MovementEnabled)
        {
            currentState = TacticState.Waiting;
            motor.SetThrottle(0f);
            return;
        }

        if (awareness != null)
        {
            awareness.Refresh();
        }

        if (pelotonReturnOverrideActive &&
            Time.time >= pelotonReturnOverrideUntil)
        {
            ClearPelotonReturnOverride();
        }

        if (IsReturningToPeloton)
        {
            currentState = TacticState.ReturningToPeloton;
            DriveTowardSpeedFraction(
                pelotonReturnTargetSpeedFraction,
                false);
            return;
        }

        if (Time.time >= nextDecisionTime)
        {
            nextDecisionTime = Time.time + decisionInterval;
            ChooseTactic();
        }

        ApplyCurrentTactic();
    }

    public void SetStrategicOrder(
        StrategicRole newRole,
        float targetSpeedFraction,
        bool canPassNormally)
    {
        strategicRole = newRole;
        strategicTargetSpeedFraction = Mathf.Clamp01(targetSpeedFraction);
        allowNormalPassing = canPassNormally;
    }

    public void SetPelotonReturnOverride(
        float targetSpeedFraction,
        float duration)
    {
        pelotonReturnOverrideActive = true;
        pelotonReturnTargetSpeedFraction = Mathf.Clamp01(targetSpeedFraction);
        pelotonReturnOverrideUntil = Time.time + Mathf.Max(0.1f, duration);
        passingUntilTime = 0f;
    }

    public void ClearPelotonReturnOverride()
    {
        pelotonReturnOverrideActive = false;
        pelotonReturnOverrideUntil = 0f;
    }

    private void ChooseTactic()
    {
        if (Time.time < passingUntilTime)
        {
            currentState = TacticState.Passing;
            return;
        }

        if (awareness != null &&
            awareness.HasRacerAhead)
        {
            if (allowNormalPassing &&
                ShouldUrgentlyPassSlowerRider() &&
                TryChangeLaneForPass())
            {
                currentState = TacticState.Passing;
                passingUntilTime = Time.time + passingDuration;
                return;
            }

            bool closeEnoughToConsiderPass =
                awareness.DistanceToRacerAhead <= passDecisionDistance;

            if (allowNormalPassing &&
                closeEnoughToConsiderPass &&
                Random.value <= passChance &&
                TryChangeLaneForPass())
            {
                currentState = TacticState.Passing;
                passingUntilTime = Time.time + passingDuration;
                return;
            }

            if (awareness.IsDrafting)
            {
                currentState = TacticState.Drafting;
                return;
            }
        }

        currentState = TacticState.Cruising;
        TryReturnTowardCenterLane();
    }

    private void ApplyCurrentTactic()
    {
        switch (currentState)
        {
            case TacticState.Drafting:
                DriveTowardSpeedFraction(
                    strategicTargetSpeedFraction * draftingSpeedMultiplier,
                    false);
                break;

            case TacticState.Passing:
                DriveTowardSpeedFraction(
                    strategicTargetSpeedFraction + passingSpeedBonus,
                    false);
                break;

            case TacticState.ReturningToPeloton:
                DriveTowardSpeedFraction(
                    pelotonReturnTargetSpeedFraction,
                    false);
                break;

            case TacticState.Waiting:
                motor.SetThrottle(0f);
                break;

            default:
                DriveTowardSpeedFraction(
                    strategicTargetSpeedFraction,
                    false);
                break;
        }
    }

    private void DriveTowardSpeedFraction(
        float speedFraction,
        bool unusedApplyStrategicOrder = false)
    {
        if (motor == null)
        {
            return;
        }

        float targetSpeed = motor.MaximumSpeed * Mathf.Clamp01(speedFraction);
        motor.SetDirectTargetSpeed(targetSpeed);
    }

    private bool ShouldUrgentlyPassSlowerRider()
    {
        if (motor == null ||
            motor.MaximumSpeed <= 0f ||
            awareness == null ||
            !awareness.HasRacerAhead ||
            awareness.RacerAhead == null ||
            awareness.DistanceToRacerAhead > passDecisionDistance)
        {
            return false;
        }

        float riderAheadSpeedFraction =
            awareness.RacerAhead.CurrentSpeed /
            motor.MaximumSpeed;

        return riderAheadSpeedFraction <
            strategicTargetSpeedFraction -
            urgentPassSpeedDifferenceFraction;
    }

    private bool TryChangeLaneForPass()
    {
        if (awareness == null ||
            motor == null ||
            motor.Track == null ||
            Time.time < nextLaneChangeTime)
        {
            return false;
        }

        int currentLane = motor.CurrentTargetLane;
        int firstDirection = Random.value < 0.5f ? -1 : 1;

        if (TryEnterLane(currentLane + firstDirection))
        {
            return true;
        }

        return TryEnterLane(currentLane - firstDirection);
    }

    private bool TryEnterLane(int desiredLane)
    {
        if (motor == null ||
            motor.Track == null ||
            awareness == null)
        {
            return false;
        }

        int safeLane = motor.Track.ClampLane(desiredLane);

        if (safeLane == motor.CurrentTargetLane)
        {
            return false;
        }

        if (!awareness.CanEnterLane(safeLane))
        {
            return false;
        }

        motor.SetLane(safeLane);
        nextLaneChangeTime = Time.time + laneChangeCooldown;
        return true;
    }

    private void TryReturnTowardCenterLane()
    {
        if (motor == null ||
            motor.Track == null ||
            awareness == null ||
            Time.time < nextLaneChangeTime ||
            Random.value > returnTowardCenterChance)
        {
            return;
        }

        int centerLane = motor.Track.LaneCount / 2;
        int currentLane = motor.CurrentTargetLane;

        if (currentLane == centerLane)
        {
            return;
        }

        int direction = currentLane < centerLane ? 1 : -1;
        TryEnterLane(currentLane + direction);
    }

    // Pull out to the side (forced — ignores occupancy) so the rider behind can
    // come through. Used when this rider rotates off the front of the pack.
    public void StepAside(int direction, int lanes)
    {
        if (motor == null)
        {
            return;
        }

        int step = direction >= 0 ? 1 : -1;
        int count = Mathf.Max(1, lanes);

        for (int i = 0; i < count; i++)
        {
            motor.ForceLaneShift(step);
        }
    }

    private void UpdateDebugFields()
    {
        debugRole = strategicRole.ToString();
        debugTargetSpeedFraction = strategicTargetSpeedFraction;
        debugReturningToPeloton = IsReturningToPeloton;
        debugAllowNormalPassing = allowNormalPassing;
    }

    // Legacy no-op methods kept so older disabled managers still compile.
    // They can be deleted after RaceTacticsManager, PelotonManager role assignment,
    // and PelotonCirculationManager are fully removed from the project.
    public void BeginAttack(float duration) { }
    public void BeginFollowAttack(float duration) { }
    public void CancelCoordinatedAttack() { }
    public void SetCirculationOverride(float targetSpeedFraction, float duration, bool canReduceSpeed) { }
    public void ClearCirculationOverride() { }
}
