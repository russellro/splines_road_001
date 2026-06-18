using UnityEngine;

// Per-zone ATP model.
//   Zones 1-3 refill the bucket, zone 4 bleeds slowly, zones 5-7 drain harder,
//   zone 7 is the sprint. Each zone's effort and ATP rate are authored below and
//   tuned in the inspector. RacerEffortInput chooses the active zone; the motor
//   passes that index in each frame via UpdateFromZone.
//
// Bonk: at or below bonkThreshold (100 of 1000 = 10%) the rider bonks and cannot
// recover until ResetEnergy. Drain still applies while bonked; regen does not.
//
// Replaces the old continuous-effort fields (maximumDrainPerSecond, exertionExponent,
// recoveryWattsThreshold, recoveryPerSecond) with the zone table. Drafting drain is
// now draftingDrainMultiplier (was draftingEffortMultiplier, same 0.70 meaning).

public class RacerEnergy : MonoBehaviour
{
    [System.Serializable]
    public struct EnergyZone
    {
        [Tooltip("Name shown on the HUD.")]
        public string label;

        [Tooltip("Effort this zone holds, 0..1 (1 = all-out). Drives speed and the WATTS% readout.")]
        [Range(0f, 1f)]
        public float effort;

        [Tooltip("ATP change per second. Positive refills the bucket, negative drains it.")]
        public float atpPerSecond;
    }

    [Header("ATP Bucket")]
    [SerializeField, Min(1f)]
    private float maximumATP = 1000f;

    [SerializeField, Min(0f)]
    private float startingATP = 1000f;

    [Header("Bonk")]
    [Tooltip("At this ATP or lower the rider bonks: no recovery until ResetEnergy. 100 of 1000 = 10%.")]
    [SerializeField, Min(0f)]
    private float bonkThreshold = 100f;

    [Header("Zones (1 to 7, lowest to highest)")]
    [Tooltip("Zones 1-3 refill, 4 bleeds slowly, 5-7 drain harder. Zone 7 is the sprint.")]
    [SerializeField]
    private EnergyZone[] zones =
    {
        new EnergyZone { label = "Recover",   effort = 0.35f, atpPerSecond =  12f },
        new EnergyZone { label = "Endurance", effort = 0.50f, atpPerSecond =   6f },
        new EnergyZone { label = "Tempo",     effort = 0.62f, atpPerSecond =   2f },
        new EnergyZone { label = "Threshold", effort = 0.74f, atpPerSecond =  -4f },
        new EnergyZone { label = "VO2 Max",   effort = 0.84f, atpPerSecond = -12f },
        new EnergyZone { label = "Anaerobic", effort = 0.93f, atpPerSecond = -24f },
        new EnergyZone { label = "Sprint",    effort = 1.00f, atpPerSecond = -45f },
    };

    [Tooltip("Zone the rider starts in (0-based). 1 = Endurance.")]
    [SerializeField, Min(0)]
    private int startingZoneIndex = 1;

    [Header("Drafting")]
    [Tooltip("Drain multiplier while drafting. 0.70 = 30% cheaper to hold a zone in the draft.")]
    [SerializeField, Range(0.05f, 1f)]
    private float draftingDrainMultiplier = 0.70f;

    [Tooltip("Recovery multiplier while drafting. 1.25 = refill 25% faster in the draft.")]
    [SerializeField, Min(1f)]
    private float draftingRecoveryMultiplier = 1.25f;

    [Tooltip("While drafting, effort/watts eases to this fraction (0.7 = 30% less). " +
             "The wheel ahead carries your speed, so you spend less to hold pace.")]
    [SerializeField, Range(0.2f, 1f)]
    private float draftingEffortMultiplier = 0.7f;

    [Header("Climbing")]
    [Tooltip("Extra drain per 1 percent of uphill grade, on drain zones only. 0.035 = +3.5% per 1%.")]
    [SerializeField, Min(0f)]
    private float uphillDrainMultiplierPerGradePercent = 0.035f;

    [Header("Speed Feel")]
    [Tooltip("Seconds for effective watts to glide between zones. Affects speed and the HUD only, " +
             "NOT the bucket (the bucket always uses the selected zone's discrete rate). 0 = instant.")]
    [SerializeField, Min(0f)]
    private float effortSmoothing = 0.15f;

    private float currentATP;
    private bool hasBonked;
    private int currentZoneIndex;
    private float requestedNormalizedWatts;
    private float effectiveNormalizedWatts;
    private float effortVelocity;

    public float CurrentATP => currentATP;
    public float MaximumATP => maximumATP;

    public float NormalizedATP =>
        maximumATP <= 0f ? 0f : currentATP / maximumATP;

    public bool IsEmpty => currentATP <= 0f;
    public bool IsBonked => hasBonked;
    public bool HasBonked => hasBonked;

    public int ZoneCount => zones != null ? zones.Length : 0;
    public int CurrentZoneIndex => currentZoneIndex;
    public int CurrentZoneNumber => currentZoneIndex + 1;

    public string CurrentZoneLabel =>
        ZoneCount == 0
            ? string.Empty
            : zones[Mathf.Clamp(currentZoneIndex, 0, zones.Length - 1)].label;

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
            Mathf.Clamp(startingATP, 0f, maximumATP);

        hasBonked =
            currentATP <= bonkThreshold;

        currentZoneIndex =
            Mathf.Clamp(startingZoneIndex, 0, Mathf.Max(0, ZoneCount - 1));

        requestedNormalizedWatts = 0f;
        effectiveNormalizedWatts = 0f;
        effortVelocity = 0f;
    }

    // Preferred entry point. RacerEffortInput selects the zone index; the motor
    // passes it here each frame with draft state, grade, and any speed limit.
    public void UpdateFromZone(
        int zoneIndex,
        bool isDrafting,
        float slopePercent,
        float speedLimit01 = 1f)
    {
        if (ZoneCount == 0)
        {
            return;
        }

        currentZoneIndex =
            Mathf.Clamp(zoneIndex, 0, zones.Length - 1);

        EnergyZone zone = zones[currentZoneIndex];

        requestedNormalizedWatts =
            Mathf.Clamp01(zone.effort);

        float target =
            Mathf.Min(
                requestedNormalizedWatts,
                Mathf.Clamp01(speedLimit01));

        // Drafting eases your effort: sitting on a wheel you hold pace at lower watts.
        // Speed is carried by the wheel/follow logic, so easing here does not drop you.
        if (isDrafting)
        {
            target *= draftingEffortMultiplier;
        }

        effectiveNormalizedWatts =
            effortSmoothing > 0f
                ? Mathf.SmoothDamp(
                    effectiveNormalizedWatts,
                    target,
                    ref effortVelocity,
                    effortSmoothing)
                : target;

        float metabolicEffort = Mathf.Clamp01(target);

        float rate = GetATPRateFromEffectiveEffort(metabolicEffort);

        if (rate < 0f)
        {
            float drainFactor =
                1f +
                Mathf.Max(0f, slopePercent) *
                uphillDrainMultiplierPerGradePercent;

            if (isDrafting)
            {
                drainFactor *= draftingDrainMultiplier;
            }

            SpendATPWithoutFailure(
                -rate * drainFactor * Time.deltaTime);
        }
        else if (rate > 0f)
        {
            float regen = rate;

            if (isDrafting)
            {
                regen *= draftingRecoveryMultiplier;
            }

            RestoreATP(
                regen * Time.deltaTime);
        }

        Debug.Log(
            $"Selected Zone: {zone.label}, " +
            $"Displayed Watts: {EffectiveWattsPercent}%, " +
            $"Metabolic Effort: {Mathf.RoundToInt(metabolicEffort * 100f)}%, " +
            $"ATP Rate: {rate}");
            }

    // Legacy shims: existing callers that pass a 0..1 watts value still compile and
    // run. The value is snapped to the nearest zone, then routed through UpdateFromZone.
    public void UpdateFromWatts(
        float normalizedWatts,
        bool isDrafting,
        float slopePercent,
        float legacyDraftingMultiplier = 0.70f)
    {
        UpdateFromZone(
            ZoneIndexFromEffort(normalizedWatts),
            isDrafting,
            slopePercent,
            1f);
    }

    public void UpdateFromWatts(
        float normalizedWatts,
        bool isDrafting,
        float slopePercent,
        float speedLimitedEffort,
        float draftingMultiplier)
    {
        UpdateFromZone(
            ZoneIndexFromEffort(normalizedWatts),
            isDrafting,
            slopePercent,
            speedLimitedEffort);
    }

    public int ZoneIndexFromEffort(float effort01)
    {
        effort01 = Mathf.Clamp01(effort01);

        int best = 0;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < ZoneCount; i++)
        {
            float distance =
                Mathf.Abs(zones[i].effort - effort01);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private float GetATPRateFromEffectiveEffort(float effort01)
    {
        if (ZoneCount == 0)
        {
            return 0f;
        }

        int effectiveZoneIndex =
            ZoneIndexFromEffort(effort01);

        return zones[effectiveZoneIndex].atpPerSecond;
    }

    public void RestoreATP(float amount)
    {
        if (amount <= 0f || hasBonked)
        {
            return;
        }

        currentATP =
            Mathf.Clamp(currentATP + amount, 0f, maximumATP);
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

        SpendATPWithoutFailure(amount);
        return true;
    }

    private void SpendATPWithoutFailure(float amount)
    {
        if (amount <= 0f)
        {
            return;
        }

        currentATP =
            Mathf.Max(0f, currentATP - amount);

        if (currentATP <= bonkThreshold)
        {
            hasBonked = true;
        }
    }
}