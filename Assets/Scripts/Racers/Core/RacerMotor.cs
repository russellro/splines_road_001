using UnityEngine;

public class RacerMotor : MonoBehaviour
{
    [Header("Race State")]
    [SerializeField]
    private bool movementEnabled;

    [Header("Track")]
    [SerializeField] private TrackPath track;

    [Header("Power")]
    [SerializeField] private RacerPower power;

    [Header("Player Energy")]
    [Tooltip("Only the player should use RacerEnergy. NPC racers ignore this even if the component exists.")]
    [SerializeField] private RacerEnergy energy;

    [Tooltip("Player input that selects the active zone (arrows = zones 1-6, sprint key = zone 7). " +
             "Drives the player's watts and speed through RacerEnergy. NPCs ignore this.")]
    [SerializeField] private RacerEffortInput effortInput;

    [SerializeField]
    private bool usePlayerEnergySystem = true;

    [Header("Forward Movement")]
    [SerializeField, Min(0f)] private float startingSpeed = 4f;
    [SerializeField, Min(0f)] private float maximumSpeed = 20f;

    [Tooltip("How quickly actual road speed rises toward the calculated target speed.")]
    [SerializeField, Min(0f)]
    private float acceleration = 4f;

    [Tooltip("How quickly actual road speed falls toward the calculated target speed.")]
    [SerializeField, Min(0f)]
    private float deceleration = 6f;

    [Header("Watts To Speed")]
    [Tooltip("Flat-road rolling speed retained at zero watts, as a fraction of maximum speed.")]
    [SerializeField, Range(0f, 0.5f)]
    private float minimumRollingSpeedFraction = 0.05f;

    [Tooltip("Controls the shape of the watts-to-speed curve. Start with 1.")]
    [SerializeField, Min(0.1f)]
    private float wattsToSpeedExponent = 1f;

    [Tooltip("Maximum uphill grade used by the arcade speed model. The displayed road grade may still be higher.")]
    [SerializeField, Range(0f, 30f)]
    private float maximumSimulatedUphillGrade = 16f;

    [Tooltip("Maximum downhill grade used by the arcade speed model.")]
    [SerializeField, Range(0f, 30f)]
    private float maximumSimulatedDownhillGrade = 12f;

    [Tooltip("Minimum speed fraction maintained while producing watts on a steep climb.")]
    [SerializeField, Range(0f, 0.5f)]
    private float minimumClimbingSpeedFraction = 0.10f;

    [Tooltip("Speed-fraction penalty per 1 percent uphill grade.")]
    [SerializeField, Min(0f)]
    private float uphillSpeedPenaltyPerGradePercent = 0.045f;

    [Tooltip("Speed-fraction bonus per 1 percent downhill grade.")]
    [SerializeField, Min(0f)]
    private float downhillSpeedBonusPerGradePercent = 0.025f;

    [Tooltip("Maximum downhill speed as a multiple of normal maximum speed.")]
    [SerializeField, Min(1f)]
    private float maximumDownhillSpeedMultiplier = 1.15f;

    [Header("Player ATP Exhaustion")]
    [Tooltip("Speed the player gradually falls toward when ATP is empty. Ignored by NPCs.")]
    [SerializeField, Min(0f)]
    private float exhaustedSpeed = 2f;

    [Tooltip("How quickly the player slows down after ATP reaches zero. Ignored by NPCs.")]
    [SerializeField, Min(0f)]
    private float exhaustionDeceleration = 6f;

    [Tooltip("Maximum watts allowed while player ATP is empty. Ignored by NPCs.")]
    [SerializeField, Range(0f, 100f)]
    private float exhaustedWattsPercent = 20f;

    [Header("Lane Movement")]
    [SerializeField] private int startingLane = 3;
    [SerializeField, Min(0f)] private float laneChangeSpeed = 5f;

    [Header("NPC Launch Lane Lock")]
    [Tooltip("Seconds after NPC movement begins before AI or manager lane-change requests are accepted.")]
    [SerializeField, Min(0f)]
    private float raceStartLaneLockDuration = 2f;

    [Header("Swimming Through Peloton")]
    [Tooltip("How much faster the rider moves sideways while holding Q.")]
    [SerializeField, Min(1f)]
    private float swimmingLaneChangeSpeedMultiplier = 2.2f;

    [Header("Nearby Racers")]
    [SerializeField]
    private RacerAwareness awareness;

    [Header("Drafting")]
    [Tooltip("Player ATP usage while drafting. 0.70 means the player uses 70 percent of normal ATP.")]
    [SerializeField, Range(0.1f, 1f)]
    private float draftingATPMultiplier = 0.70f;

    [Tooltip("Player-only: drafting may lower selected watts. NPCs ignore this.")]
    [SerializeField]
    private bool latchDraftingWattsReduction;

    [Header("Starting Grid")]
    [Tooltip("Distance around the track where the starting line is located.")]
    [SerializeField, Min(0f)]
    private float startingLineDistance;

    [Tooltip("Distance behind the starting line for this racer.")]
    [SerializeField, Min(0f)]
    private float startingGridOffset;

    private bool isNpcRacer;
    private PlayerWheelLock playerWheelLock;
    private float npcLaneChangesLockedUntil;

    private float normalizedProgress;
    private float currentSpeed;
    private float targetSpeed;
    private float throttleInput;
    private float currentSlopePercent;

    private int targetLane;
    private float currentSidewaysOffset;

    private float distanceAlongTrack;
    private float totalDistanceTravelled;

    private float laneBias;
    private bool isSwimming;

    private float temporaryThrottleBoostUntil;
    private float temporaryThrottleFloor;

    // Tracks whether this racer has already advanced during the current frame.
    // Followers use this to predict only the movement that has not happened yet.
    private int lastProgressUpdateFrame = -1;

    private bool useExternalSpacingResolver;

    private bool useDirectSpeedControl;
    private float directTargetSpeed;

    public float NormalizedProgress => normalizedProgress;
    public float CurrentSpeed => currentSpeed;
    public float TargetSpeed => targetSpeed;
    public float CurrentSlopePercent => currentSlopePercent;
    public int CurrentTargetLane => targetLane;
    public TrackPath Track => track;
    public float DistanceAlongTrack => distanceAlongTrack;
    public float TotalDistanceTravelled => totalDistanceTravelled;
    public float MaximumSpeed => maximumSpeed;
    public bool MovementEnabled => movementEnabled;

    public float UnwrappedTrackDistance =>
        startingLineDistance -
        startingGridOffset +
        totalDistanceTravelled;

    public float RaceDistanceFromStart =>
        totalDistanceTravelled -
        startingGridOffset;

    public float CurrentSidewaysOffset =>
        currentSidewaysOffset;

    public float WattsPercent =>
        ShouldUsePlayerEnergy()
            ? energy.EffectiveWattsPercent
            : power != null
                ? power.WattsPercent
                : 0f;

    public float NormalizedWatts =>
        GetMotorNormalizedWatts();

    public float LaneBias => laneBias;
    public bool IsSwimming => isSwimming;
    public int LastProgressUpdateFrame => lastProgressUpdateFrame;

    public bool NpcLaneChangesLocked =>
        isNpcRacer &&
        (!movementEnabled ||
        Time.time < npcLaneChangesLockedUntil);

    // Kept so older HUD/debug scripts compile. Deep fatigue is intentionally removed.
    public float DeepEffortFatigue => 0f;

    private void Awake()
    {
        isNpcRacer =
            GetComponent<NPCRacerController>() != null;

        playerWheelLock =
            GetComponent<PlayerWheelLock>();
    }

    private void Start()
    {
        if (track == null)
        {
            Debug.LogError(
                $"{name} does not have a TrackPath assigned.");

            enabled = false;
            return;
        }

        if (power == null)
        {
            power =
                GetComponent<RacerPower>();
        }

        if (power == null)
        {
            power =
                gameObject.AddComponent<RacerPower>();

            Debug.LogWarning(
                $"{name}: RacerPower was missing. A default RacerPower component was added at runtime. " +
                "Add RacerPower to the prefab to tune this rider in the Inspector.");
        }

        if (energy == null)
        {
            energy =
                GetComponent<RacerEnergy>();
        }

        if (effortInput == null)
        {
            effortInput =
                GetComponent<RacerEffortInput>();
        }

        if (ShouldUsePlayerEnergy() &&
            effortInput == null)
        {
            Debug.LogWarning(
                $"{name}: player uses RacerEnergy but has no RacerEffortInput. " +
                "Zone and sprint input will not drive watts. Add RacerEffortInput to this " +
                "GameObject or assign it in the RacerMotor inspector.");
        }

        if (awareness == null)
        {
            awareness =
                GetComponent<RacerAwareness>();
        }

        targetLane =
            track.ClampLane(
                startingLane);

        currentSidewaysOffset =
            track.GetLaneOffset(
                targetLane) +
            laneBias;

        ResetToStartingGrid();
    }

    private void Update()
    {
        if (!movementEnabled)
        {
            ApplyTrackPose();
            return;
        }

        if (awareness != null)
        {
            awareness.Refresh();
        }

        UpdateSlope();
        UpdatePower();
        LatchDraftingWattsReduction();
        UpdateEnergy();
        UpdateSpeed();

        if (!useExternalSpacingResolver)
        {
            UpdateProgress();
            UpdateLaneOffset();
            ApplyTrackPose();
        }
    }

    public void ConfigureStartingGrid(
        TrackPath newTrack,
        int lane,
        float lineDistance,
        float gridOffset)
    {
        if (newTrack != null)
        {
            track = newTrack;
        }

        startingLane = lane;
        startingLineDistance = Mathf.Max(0f, lineDistance);
        startingGridOffset = Mathf.Max(0f, gridOffset);
    }

    public void SetMovementEnabled(bool isEnabled)
    {
        bool wasEnabled =
            movementEnabled;

        movementEnabled =
            isEnabled;

        if (!movementEnabled)
        {
            throttleInput = 0f;
            currentSpeed = 0f;
            targetSpeed = 0f;
            return;
        }

        if (!wasEnabled &&
            isNpcRacer)
        {
            npcLaneChangesLockedUntil =
                Time.time +
                raceStartLaneLockDuration;
        }

        currentSpeed =
            Mathf.Clamp(
                startingSpeed,
                0f,
                maximumSpeed);
    }

    // This remains named SetThrottle so existing player input and NPC AI keep working.
    // It changes watts rather than directly setting road speed.
    public void SetThrottle(float amount)
    {
        throttleInput =
            Mathf.Clamp(
                amount,
                -1f,
                1f);
    }

    public void SetDirectTargetSpeed(float speed)
    {
        useDirectSpeedControl = true;
        directTargetSpeed = Mathf.Clamp(speed, 0f, maximumSpeed);
    }

    public void ChangeLane(int direction)
    {
        if (track == null ||
            ShouldRejectNpcLaneChange())
        {
            return;
        }

        int desiredLane =
            track.ClampLane(
                targetLane +
                direction);

        if (desiredLane == targetLane)
        {
            return;
        }

        if (awareness != null &&
            !awareness.CanEnterLane(
                desiredLane))
        {
            return;
        }

        targetLane =
            desiredLane;
    }

    public void SetLane(int lane)
    {
        if (track == null ||
            ShouldRejectNpcLaneChange())
        {
            return;
        }

        int desiredLane =
            track.ClampLane(
                lane);

        if (desiredLane == targetLane)
        {
            return;
        }

        if (awareness != null &&
            !awareness.CanEnterLane(
                desiredLane))
        {
            return;
        }

        targetLane =
            desiredLane;
    }

    public bool ForceLaneShift(int direction)
    {
        if (track == null ||
            direction == 0 ||
            ShouldRejectNpcLaneChange())
        {
            return false;
        }

        int desiredLane =
            track.ClampLane(
                targetLane +
                Mathf.Clamp(
                    direction,
                    -1,
                    1));

        if (desiredLane == targetLane)
        {
            return false;
        }

        targetLane =
            desiredLane;

        return true;
    }

    public void SetUseExternalSpacingResolver(
    bool shouldUseExternalSpacingResolver)
    {
        useExternalSpacingResolver =
            shouldUseExternalSpacingResolver;
    }

    private bool ShouldRejectNpcLaneChange()
    {
        return isNpcRacer &&
            (!movementEnabled ||
            Time.time < npcLaneChangesLockedUntil);
    }

    public void SetSpeed(float newSpeed)
    {
        currentSpeed =
            Mathf.Clamp(
                newSpeed,
                0f,
                maximumSpeed *
                maximumDownhillSpeedMultiplier);
    }

    private bool ShouldUsePlayerEnergy()
    {
        return usePlayerEnergySystem &&
            !isNpcRacer &&
            energy != null;
    }

    // The player's watts now come from the selected zone (via RacerEnergy), not RacerPower.
    // NPCs keep using RacerPower exactly as before.
    private float GetMotorNormalizedWatts()
    {
        if (ShouldUsePlayerEnergy())
        {
            return energy.EffectiveNormalizedWatts;
        }

        return power != null
            ? power.NormalizedWatts
            : 0f;
    }

    private void UpdateSlope()
    {
        currentSlopePercent =
            track != null
                ? track.GetSlopePercentAtDistance(
                    distanceAlongTrack)
                : 0f;
    }

    private void UpdatePower()
    {
        if (power == null)
        {
            return;
        }

        if (ShouldUsePlayerEnergy() &&
            energy.IsEmpty)
        {
            power.SetWattsPercent(
                Mathf.Min(
                    power.WattsPercent,
                    exhaustedWattsPercent));
        }

        float effectiveThrottle =
            throttleInput;

        if (Time.time <
            temporaryThrottleBoostUntil)
        {
            power.EnsureMinimumWattsPercent(
                temporaryThrottleFloor *
                100f);

            effectiveThrottle =
                Mathf.Max(
                    effectiveThrottle,
                    0f);
        }

        power.AdjustWatts(
            effectiveThrottle);
    }

    private void LatchDraftingWattsReduction()
    {
        if (isNpcRacer ||
            !latchDraftingWattsReduction ||
            power == null ||
            awareness == null ||
            !awareness.IsDrafting ||
            !awareness.HasRacerAhead ||
            awareness.RacerAhead == null ||
            awareness.DistanceToRacerAhead >
            awareness.SpeedMatchingDistance ||
            maximumSpeed <= 0f)
        {
            return;
        }

        float riderAheadSpeedFraction =
            Mathf.Clamp01(
                awareness.RacerAhead.CurrentSpeed /
                maximumSpeed);

        float wattsNeededToFollow =
            riderAheadSpeedFraction *
            100f;

        power.SetWattsPercent(
            Mathf.Min(
                power.WattsPercent,
                wattsNeededToFollow));
    }

    private void UpdateSpeed()
    {
        if (useDirectSpeedControl)
        {
            float limitedDirectSpeed =
                ApplyFollowSpeedLimit(directTargetSpeed);   // add this

            float directRate =
                currentSpeed < limitedDirectSpeed
                    ? acceleration
                    : deceleration;

            currentSpeed = Mathf.MoveTowards(
                currentSpeed,
                limitedDirectSpeed,   // was directTargetSpeed
                directRate * Time.deltaTime);

            return;
        }
        if (power == null)
        {
            return;
        }

        bool isWheelLocked =
            playerWheelLock != null &&
            playerWheelLock.IsWheelLocked;

        if (ShouldUsePlayerEnergy() &&
            energy.IsEmpty &&
            !isWheelLocked)
        {
            targetSpeed =
                Mathf.Min(
                    CalculateTargetSpeed(),
                    exhaustedSpeed);

            currentSpeed =
                Mathf.MoveTowards(
                    currentSpeed,
                    targetSpeed,
                    exhaustionDeceleration *
                    Time.deltaTime);

            return;
        }

        targetSpeed = CalculateTargetSpeed();
        targetSpeed = ApplyFollowSpeedLimit(targetSpeed);

        float speedChangeRate =
            currentSpeed <
            targetSpeed
                ? acceleration
                : deceleration;

        currentSpeed =
            Mathf.MoveTowards(
                currentSpeed,
                targetSpeed,
                speedChangeRate *
                Time.deltaTime);
    }

    private float CalculateTargetSpeed()
    {
        float normalizedWatts =
            GetMotorNormalizedWatts();

        float poweredFraction =
            Mathf.Pow(
                normalizedWatts,
                wattsToSpeedExponent);

        float targetSpeedFraction =
            minimumRollingSpeedFraction +
            poweredFraction *
            (1f -
            minimumRollingSpeedFraction);

        float simulatedUphillGrade =
            Mathf.Clamp(
                currentSlopePercent,
                0f,
                maximumSimulatedUphillGrade);

        float simulatedDownhillGrade =
            Mathf.Clamp(
                -currentSlopePercent,
                0f,
                maximumSimulatedDownhillGrade);

        targetSpeedFraction -=
            simulatedUphillGrade *
            uphillSpeedPenaltyPerGradePercent;

        targetSpeedFraction +=
            simulatedDownhillGrade *
            downhillSpeedBonusPerGradePercent;

        if (normalizedWatts > 0.01f &&
            simulatedUphillGrade > 0f)
        {
            targetSpeedFraction =
                Mathf.Max(
                    targetSpeedFraction,
                    minimumClimbingSpeedFraction);
        }

        targetSpeedFraction =
            Mathf.Clamp(
                targetSpeedFraction,
                0f,
                maximumDownhillSpeedMultiplier);

        return maximumSpeed *
            targetSpeedFraction;
    }

    private void UpdateEnergy()
    {
        if (!ShouldUsePlayerEnergy() ||
            power == null)
        {
            return;
        }

        bool isDrafting =
            awareness != null &&
            awareness.IsDrafting;

        if (IsPlayerWheelLocked())
        {
            isDrafting = true;
        }

        // How much a slower racer directly ahead is capping our road speed (1 = no cap).
        float riderAheadLimit01 = 1f;

        if (maximumSpeed > 0f &&
            awareness != null &&
            awareness.HasRacerAhead &&
            awareness.RacerAhead != null &&
            awareness.DistanceToRacerAhead <=
            awareness.SpeedMatchingDistance)
        {
            riderAheadLimit01 =
                Mathf.Clamp01(
                    awareness.RacerAhead.CurrentSpeed /
                    maximumSpeed);
        }

        // Player: the selected zone drives both watts and ATP. The cap above only bites
        // when boxed in tight behind a slower rider, so it is passed as the speed limit.
        if (effortInput != null)
        {
            energy.UpdateFromZone(
                effortInput.CurrentZoneIndex,
                isDrafting,
                currentSlopePercent,
                riderAheadLimit01);
        }
        else
        {
            // Fallback for a player with no RacerEffortInput assigned: old watts path.
            energy.UpdateFromWatts(
                power.NormalizedWatts,
                isDrafting,
                currentSlopePercent,
                Mathf.Min(power.NormalizedWatts, riderAheadLimit01),
                draftingATPMultiplier);
        }
    }

    private void UpdateProgress()
    {
        if (track == null ||
            track.Length <= 0f)
        {
            return;
        }

        float distanceMoved =
            currentSpeed *
            Time.deltaTime;

        totalDistanceTravelled +=
            distanceMoved;

        lastProgressUpdateFrame =
            Time.frameCount;

        distanceAlongTrack =
            Mathf.Repeat(
                UnwrappedTrackDistance,
                track.Length);

        normalizedProgress =
            track.GetProgressAtDistance(
                distanceAlongTrack);
    }

    public void ApplyResolvedMovement(
    float distanceMoved)
    {
        if (track == null ||
            track.Length <= 0f)
        {
            return;
        }

        totalDistanceTravelled +=
            Mathf.Max(
                0f,
                distanceMoved);

        lastProgressUpdateFrame =
            Time.frameCount;

        distanceAlongTrack =
            Mathf.Repeat(
                UnwrappedTrackDistance,
                track.Length);

        normalizedProgress =
            track.GetProgressAtDistance(
                distanceAlongTrack);

        UpdateLaneOffset();
        ApplyTrackPose();
    }

    private bool SharesCollisionLaneSpace(
    RacerMotor otherRacer)
    {
        if (track == null ||
            otherRacer == null)
        {
            return false;
        }

        float lateralClearance =
            track.LaneSpacing *
            0.45f;

        float lateralGap =
            Mathf.Abs(
                otherRacer.CurrentSidewaysOffset -
                currentSidewaysOffset);

        return lateralGap <
            lateralClearance;
    }

    private void UpdateLaneOffset()
    {
        if (track == null)
        {
            return;
        }

        float desiredOffset =
            track.GetLaneOffset(
                targetLane) +
            laneBias;

        float sidewaysSpeed =
            laneChangeSpeed;

        if (isSwimming)
        {
            sidewaysSpeed *=
                swimmingLaneChangeSpeedMultiplier;
        }

        currentSidewaysOffset =
            Mathf.MoveTowards(
                currentSidewaysOffset,
                desiredOffset,
                sidewaysSpeed *
                Time.deltaTime);
    }

    private void ApplyTrackPose()
    {
        if (track == null)
        {
            return;
        }

        if (track.TryGetPose(
            normalizedProgress,
            currentSidewaysOffset,
            out Vector3 position,
            out Quaternion rotation))
        {
            transform.SetPositionAndRotation(
                position,
                rotation);
        }
    }

    public void ResetToStartingGrid()
    {
        if (track == null)
        {
            Debug.LogError(
                $"{name}: Reset failed because TrackPath is missing.");

            return;
        }

        if (track.Length <= 0f)
        {
            Debug.LogError(
                $"{name}: Reset failed because the track length is zero.");

            return;
        }

        totalDistanceTravelled = 0f;

        distanceAlongTrack =
            Mathf.Repeat(
                startingLineDistance -
                startingGridOffset,
                track.Length);

        normalizedProgress =
            track.GetProgressAtDistance(
                distanceAlongTrack);

        currentSlopePercent =
            track.GetSlopePercentAtDistance(
                distanceAlongTrack);

        currentSpeed = 0f;
        targetSpeed = 0f;
        throttleInput = 0f;
        lastProgressUpdateFrame = -1;

        targetLane =
            track.ClampLane(
                startingLane);

        currentSidewaysOffset =
            track.GetLaneOffset(
                targetLane) +
            laneBias;

        ApplyTrackPose();

        Debug.Log(
            $"{name}: moved to starting grid at " +
            $"distance {distanceAlongTrack}.");
    }

    private void OnEnable()
    {
        RacerRegistry.Register(this);
    }

    private void OnDisable()
    {
        RacerRegistry.Unregister(this);
    }

    public void SetLaneBias(float newLaneBias)
    {
        laneBias =
            newLaneBias;
    }

    public void StartTemporaryThrottleBoost(
        float throttleFloor,
        float duration)
    {
        temporaryThrottleFloor =
            Mathf.Clamp(
                throttleFloor,
                0f,
                1f);

        temporaryThrottleBoostUntil =
            Time.time +
            Mathf.Max(
                0f,
                duration);
    }

    public void SetSwimming(bool swimming)
    {
        isSwimming =
            swimming;
    }


    private float ApplyFollowSpeedLimit(
    float targetSpeed)
    {
        if (awareness == null ||
            awareness.RacerAhead == null)
        {
            return targetSpeed;
        }

        RacerMotor leader =
            awareness.RacerAhead;

        if (leader == null ||
            leader.Track != track)
        {
            return targetSpeed;
        }

        float gap =
            awareness.DistanceToRacerAhead;

        float minimumGap =
            Mathf.Max(
                0.75f,
                awareness.MinimumFollowingDistance);

        float brakingGap =
            minimumGap + 5f;

        if (gap >= brakingGap)
        {
            return targetSpeed;
        }

        float gap01 =
            Mathf.InverseLerp(
                minimumGap,
                brakingGap,
                gap);

        float leaderSpeed =
            leader.CurrentSpeed;

        float closeFollowSpeed =
            leaderSpeed * 0.98f;

        float limitedSpeed =
            Mathf.Lerp(
                closeFollowSpeed,
                targetSpeed,
                gap01);

        return Mathf.Min(
            targetSpeed,
            limitedSpeed);
    }

    private bool IsPlayerWheelLocked()
    {
        return playerWheelLock != null &&
            playerWheelLock.IsWheelLocked;
    }

    private bool IsNPCRacer()
    {
        return isNpcRacer;
    }

    public void NudgeSpeedToward(
    float desiredSpeed,
    float speedChangePerSecond)
    {
        float maximumAllowedSpeed =
            maximumSpeed *
            maximumDownhillSpeedMultiplier;

        float clampedDesiredSpeed =
            Mathf.Clamp(
                desiredSpeed,
                0f,
                maximumAllowedSpeed);

        currentSpeed =
            Mathf.MoveTowards(
                currentSpeed,
                clampedDesiredSpeed,
                Mathf.Max(0f, speedChangePerSecond) *
                Time.deltaTime);
    }
    public void LimitSpeedTo(float maximumSpeed)
    {
        float capped = Mathf.Max(0f, maximumSpeed);
        if (currentSpeed > capped)
        {
            currentSpeed = capped;
        }
    }
}