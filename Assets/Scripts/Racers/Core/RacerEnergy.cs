using UnityEngine;

public class RacerEnergy : MonoBehaviour
{
    [Header("ATP Bucket")]
    [SerializeField, Min(1f)]
    private float maximumATP = 1000f;

    [SerializeField, Min(0f)]
    private float startingATP = 1000f;

    [Header("Bonk Settings")]
    [Tooltip("Once ATP reaches this value or lower, natural recovery is disabled until the race resets.")]
    [SerializeField, Min(0f)]
    private float bonkThreshold = 100f;

    [Header("ATP Consumption")]
    [Tooltip("ATP used each second at 100 percent effective watts on flat road.")]
    [SerializeField, Min(0f)]
    private float maximumDrainPerSecond = 20f;

    [Tooltip("Higher values make high watts disproportionately expensive.")]
    [SerializeField, Min(1f)]
    private float exertionExponent = 2f;

    [Tooltip("Additional ATP drain multiplier added per 1 percent uphill grade.")]
    [SerializeField, Min(0f)]
    private float uphillDrainMultiplierPerGradePercent = 0.035f;

    [Header("Drafting")]
    [Tooltip("Actual effort multiplier while protected behind another rider. For example, 0.70 means drafting reduces required watts by 30 percent.")]
    [SerializeField, Range(0.05f, 1f)]
    private float draftingEffortMultiplier = 0.70f;

    [Header("ATP Recovery")]
    [Tooltip("ATP recovers when effective watts are at or below this fraction of maximum effort.")]
    [SerializeField, Range(0f, 1f)]
    private float recoveryWattsThreshold = 0.40f;

    [Tooltip("ATP restored each second while riding below the recovery threshold.")]
    [SerializeField, Min(0f)]
    private float recoveryPerSecond = 4f;

    [Tooltip("Additional recovery multiplier while drafting.")]
    [SerializeField, Min(1f)]
    private float draftingRecoveryMultiplier = 1.25f;

    private float currentATP;
    private bool hasBonked;
    private float requestedNormalizedWatts;
    private float effectiveNormalizedWatts;

    public float CurrentATP => currentATP;
    public float MaximumATP => maximumATP;

    public float NormalizedATP =>
        maximumATP <= 0f
            ? 0f
            : currentATP / maximumATP;

    public bool IsEmpty => currentATP <= 0f;
    public bool IsBonked => hasBonked;
    public bool HasBonked => hasBonked;

    public float RequestedNormalizedWatts => requestedNormalizedWatts;
    public float EffectiveNormalizedWatts => effectiveNormalizedWatts;
    public int EffectiveWattsPercent => Mathf.RoundToInt(effectiveNormalizedWatts * 100f);

    private void Awake()
    {
        ResetEnergy();
    }

    public void ResetEnergy()
    {
        currentATP =
            Mathf.Clamp(
                startingATP,
                0f,
                maximumATP);

        hasBonked =
            currentATP <= bonkThreshold;

        requestedNormalizedWatts = 0f;
        effectiveNormalizedWatts = 0f;
    }

    // Backward-compatible version for existing callers.
    // This already lowers actual watts while drafting, but cannot yet account
    // for being speed-limited behind a slower rider.
    public void UpdateFromWatts(
        float normalizedWatts,
        bool isDrafting,
        float slopePercent,
        float legacyDraftingMultiplier = 0.70f)
    {
        UpdateFromWatts(
            normalizedWatts,
            isDrafting,
            slopePercent,
            normalizedWatts,
            legacyDraftingMultiplier);
    }

    // Preferred version. RacerMotor should pass its speed-limited effort as
    // the fourth argument and the drafting reduction as the fifth argument.
    public void UpdateFromWatts(
        float normalizedWatts,
        bool isDrafting,
        float slopePercent,
        float speedLimitedEffort,
        float draftingMultiplier)
    {
        requestedNormalizedWatts =
            Mathf.Clamp01(
                normalizedWatts);

        speedLimitedEffort =
            Mathf.Clamp01(
                speedLimitedEffort);

        effectiveNormalizedWatts =
            Mathf.Min(
                requestedNormalizedWatts,
                speedLimitedEffort);

        if (isDrafting)
        {
            float protectedEffortMultiplier =
                draftingMultiplier > 0f
                    ? Mathf.Clamp(
                        draftingMultiplier,
                        0.05f,
                        1f)
                    : draftingEffortMultiplier;

            effectiveNormalizedWatts *=
                protectedEffortMultiplier;
        }

        if (effectiveNormalizedWatts <=
            recoveryWattsThreshold)
        {
            float recoveryMultiplier =
                isDrafting
                    ? draftingRecoveryMultiplier
                    : 1f;

            RestoreATP(
                recoveryPerSecond *
                recoveryMultiplier *
                Time.deltaTime);

            return;
        }

        if (currentATP <= 0f)
        {
            return;
        }

        float exertion =
            Mathf.Pow(
                effectiveNormalizedWatts,
                exertionExponent);

        float uphillMultiplier =
            1f +
            Mathf.Max(
                0f,
                slopePercent) *
            uphillDrainMultiplierPerGradePercent;

        float drain =
            maximumDrainPerSecond *
            exertion *
            uphillMultiplier *
            Time.deltaTime;

        SpendATPWithoutFailure(
            drain);
    }

    // Backward-compatible method for older scripts during migration.
    public void ConsumeATP(
        float currentSpeed,
        float maximumSpeed,
        float efficiencyMultiplier = 1f)
    {
        if (currentSpeed <= 0f ||
            maximumSpeed <= 0f ||
            currentATP <= 0f)
        {
            return;
        }

        effectiveNormalizedWatts =
            Mathf.Clamp01(
                currentSpeed /
                maximumSpeed);

        requestedNormalizedWatts =
            effectiveNormalizedWatts;

        float exertion =
            Mathf.Pow(
                effectiveNormalizedWatts,
                exertionExponent);

        float drain =
            maximumDrainPerSecond *
            exertion *
            efficiencyMultiplier *
            Time.deltaTime;

        SpendATPWithoutFailure(
            drain);
    }

    public void RestoreATP(float amount)
    {
        if (amount <= 0f ||
            hasBonked)
        {
            return;
        }

        currentATP =
            Mathf.Clamp(
                currentATP + amount,
                0f,
                maximumATP);
    }

    public bool TrySpendATP(float amount)
    {
        if (amount <= 0f)
        {
            return true;
        }

        if (currentATP < amount)
        {
            return false;
        }

        SpendATPWithoutFailure(
            amount);

        return true;
    }

    private void SpendATPWithoutFailure(float amount)
    {
        if (amount <= 0f)
        {
            return;
        }

        currentATP =
            Mathf.Max(
                0f,
                currentATP - amount);

        if (currentATP <= bonkThreshold)
        {
            hasBonked = true;
        }
    }
}
