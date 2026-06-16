using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Clean MVP peloton role owner.
/// Attach this to one empty GameObject in the race scene.
/// 
/// Purpose:
/// - Assign simple peloton roles from current race order.
/// - Keep the main pack together.
/// - Pull accidental solo riders back into the pack.
/// - Rotate the front rider.
/// - Gently close gaps in the body of the peloton.
/// 
/// This replaces the role-assignment parts of RaceTacticsManager,
/// PelotonManager, and PelotonCirculationManager for the rebuild phase.
/// It does not use NPC ATP.
/// </summary>
public class SimplePelotonRoleAssigner : MonoBehaviour
{
    [Header("Refresh")]
    [SerializeField, Min(0.05f)]
    private float refreshInterval = 0.25f;

    [Header("Pack Detection")]
    [Tooltip("Maximum distance gap between neighboring riders for them to count as one connected pack.")]
    [SerializeField, Min(0.5f)]
    private float packLinkDistance = 9f;

    [Tooltip("A rider this far ahead of the detected pack front is treated as accidentally detached and slowed.")]
    [SerializeField, Min(0f)]
    private float detachedAheadGap = 6f;

    [Tooltip("A rider this far behind the detected pack rear receives catch-up speed.")]
    [SerializeField, Min(0f)]
    private float droppedBehindGap = 15f;

    [Header("Peloton Speeds")]
    [Tooltip("Normal main-pack target speed fraction.")]
    [SerializeField, Range(0f, 1f)]
    private float pelotonBodySpeed = 0.52f;

    [Header("Pace Rhythm")]
    [Tooltip("Per-rider pace variation so the pack breathes and riders shuffle. Open-loop (no feedback), so it can't oscillate.")]
    [SerializeField]
    private bool usePaceRhythm = true;

    [Tooltip("Size of each rider's pace swing as a fraction of pack pace. 0.03 = ±3%. Bigger = more shuffling, but keep it below cohesionMaxBoost or the breathing can out-spread the gap closing.")]
    [SerializeField, Range(0f, 0.15f)]
    private float rhythmAmplitude = 0.03f;

    [Tooltip("Shortest personal pace cycle, seconds. Shorter = quicker jockeying.")]
    [SerializeField, Min(2f)]
    private float rhythmMinPeriod = 12f;

    [Tooltip("Longest personal pace cycle, seconds.")]
    [SerializeField, Min(2f)]
    private float rhythmMaxPeriod = 28f;

    [Tooltip("Speed used when a rider falls off the back and needs to catch the pack.")]
    [SerializeField, Range(0f, 1f)]
    private float catchUpSpeed = 0.62f;

    [Tooltip("Speed used when an accidental front rider must drift back into the peloton.")]
    [SerializeField, Range(0f, 1f)]
    private float detachedReturnSpeed = 0.45f;

    [Header("Front Rotation")]
    [SerializeField]
    private bool useFrontRotation = true;

    [Tooltip("Pull the rotating-off leader out to the side so the next rider comes through.")]
    [SerializeField]
    private bool peelOffOnRotation = true;

    [Tooltip("How many lanes the rotating-off leader pulls aside.")]
    [SerializeField, Range(1, 3)]
    private int rotationPeelLanes = 2;

    private int peelSign = 1;

    [SerializeField, Min(1f)]
    private float frontRotationInterval = 8f;

    [Tooltip("How many near-front riders are eligible to become the next front rider.")]
    [SerializeField, Min(1)]
    private int frontRotationCandidateCount = 4;

    [Tooltip("Only riders within this distance of the main-pack front can be selected for rotation.")]
    [SerializeField, Min(0.5f)]
    private float frontCandidateWindow = 6f;

    [Tooltip("How long the old front rider eases off after rotating out.")]
    [SerializeField, Min(0f)]
    private float rotateOffDuration = 3f;

    [Tooltip("Speed used by the old front rider while rotating off.")]
    [SerializeField, Range(0f, 1f)]
    private float rotateOffSpeed = 0.49f;

    [Header("Cohesion")]
    [Tooltip("Every non-front rider closes the gap to the rider directly ahead by easing in a speed boost. Proportional (no on/off threshold), so it can't surge.")]
    [SerializeField]
    private bool useGapClosing = true;

    [Tooltip("Gap each rider holds behind the rider directly ahead. Keep comfortably above the resolver's NPC min gap so riders settle before they hit the resolver's hard floor.")]
    [SerializeField, Min(0.5f)]
    private float cohesionTargetGap = 2f;

    [Tooltip("Speed boost per metre of excess gap, as a fraction of pace. Higher = tighter, faster-closing pack. Lower it if you see pulsing.")]
    [SerializeField, Range(0f, 0.1f)]
    private float cohesionGain = 0.02f;

    [Tooltip("Cap on the gap-closing boost (fraction of pace). Keep this above rhythmAmplitude so cohesion always beats the breathing.")]
    [SerializeField, Range(0f, 0.2f)]
    private float cohesionMaxBoost = 0.1f;

    [SerializeField] private NPCRacerController currentFrontRider;
    [SerializeField] private string currentFrontRiderName = "None";
    [SerializeField] private NPCRacerController rotatingOffRider;
    [SerializeField] private string rotatingOffRiderName = "None";

    private float nextFrontRotationTime;
    private float rotatingOffUntil;

    [Header("Cohesion Debug")]
    [SerializeField] private int closingRiderCount;
    [SerializeField] private string largestPackGapBehindName = "None";
    [SerializeField] private float largestPackGap;

    [Header("Debug")]
    [SerializeField] private int detectedNPCCount;
    [SerializeField] private int mainPackCount;
    [SerializeField] private int pelotonFrontCount;
    [SerializeField] private int pelotonBodyCount;
    [SerializeField] private int detachedAheadCount;
    [SerializeField] private int droppedBehindCount;
    [SerializeField] private string mainPackFrontName = "None";
    [SerializeField] private float mainPackRearDistance;
    [SerializeField] private float mainPackFrontDistance;

    private readonly List<NPCRacerController> npcs =
        new List<NPCRacerController>();

    private readonly List<NPCRacerController> orderedRearToFront =
        new List<NPCRacerController>();

    private readonly List<NPCRacerController> mainPack =
        new List<NPCRacerController>();

    private float nextRefreshTime;

    private void Update()
    {
        if (Time.time < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime =
            Time.time + refreshInterval;

        RefreshNPCs();
        FindMainPack();
        AssignRoles();
    }

    private void RefreshNPCs()
    {
        npcs.Clear();

        NPCRacerController[] found =
            FindObjectsByType<NPCRacerController>(
                FindObjectsSortMode.None);

        foreach (NPCRacerController npc in found)
        {
            if (npc == null ||
                npc.Motor == null ||
                !npc.Motor.MovementEnabled)
            {
                continue;
            }

            npcs.Add(npc);
        }

        detectedNPCCount =
            npcs.Count;

        orderedRearToFront.Clear();
        orderedRearToFront.AddRange(npcs);

        orderedRearToFront.Sort(
            (a, b) => a.RaceDistance.CompareTo(b.RaceDistance));
    }

    private void FindMainPack()
    {
        mainPack.Clear();

        if (orderedRearToFront.Count == 0)
        {
            mainPackCount = 0;
            mainPackFrontName = "None";
            mainPackRearDistance = 0f;
            mainPackFrontDistance = 0f;
            return;
        }

        int bestStart = 0;
        int bestCount = 1;

        int currentStart = 0;
        int currentCount = 1;

        for (int i = 1; i < orderedRearToFront.Count; i++)
        {
            float gap =
                orderedRearToFront[i].RaceDistance -
                orderedRearToFront[i - 1].RaceDistance;

            if (gap <= packLinkDistance)
            {
                currentCount++;
            }
            else
            {
                if (currentCount > bestCount)
                {
                    bestStart = currentStart;
                    bestCount = currentCount;
                }

                currentStart = i;
                currentCount = 1;
            }
        }

        if (currentCount > bestCount)
        {
            bestStart = currentStart;
            bestCount = currentCount;
        }

        for (int i = bestStart; i < bestStart + bestCount; i++)
        {
            mainPack.Add(orderedRearToFront[i]);
        }

        mainPackCount =
            mainPack.Count;

        NPCRacerController rear =
            mainPack[0];

        NPCRacerController front =
            mainPack[mainPack.Count - 1];

        mainPackRearDistance =
            rear.RaceDistance;

        mainPackFrontDistance =
            front.RaceDistance;

        mainPackFrontName =
            front.name;
    }

    private void AssignRoles()
    {
        pelotonFrontCount = 0;
        pelotonBodyCount = 0;
        detachedAheadCount = 0;
        droppedBehindCount = 0;

        if (mainPack.Count == 0)
        {
            return;
        }

        UpdateFrontRotation();

        closingRiderCount = 0;
        largestPackGap = 0f;
        largestPackGapBehindName = "None";

        // mainPack is ordered rear -> front (ascending race distance), so the
        // rider directly ahead of mainPack[i] is the next one up the list, and the
        // front of the pack has nobody ahead. That frontmost rider sets a steady
        // pace; everyone else simply closes the gap to the rider directly ahead.
        for (int i = 0; i < mainPack.Count; i++)
        {
            NPCRacerController npc =
                mainPack[i];

            if (npc == null)
            {
                continue;
            }

            npc.ClearPelotonReturnOverride();

            NPCRacerController globalAhead =
                RiderAheadInPack(i);

            // Close on the rider directly ahead in the SAME lane so each lane packs
            // tight, falling back to the nearest rider ahead in any lane (which
            // pulls a lane-leader up level with the pack front). This lets riders
            // share race-distance bands across lanes instead of forming one long
            // single-file staircase, so the bunch is short front-to-back.
            NPCRacerController riderAhead =
                SameLaneRiderAhead(
                    i,
                    npc.Motor != null
                        ? npc.Motor.CurrentTargetLane
                        : int.MinValue)
                ?? globalAhead;

            bool isFront =
                npc == currentFrontRider;

            // Only the single front-most rider in the whole pack sets the pace;
            // every lane-leader behind it still closes up onto the front.
            bool isAnchor =
                globalAhead == null;

            bool isRotatingOff =
                npc == rotatingOffRider &&
                Time.time < rotatingOffUntil;

            float targetSpeed;

            if (isRotatingOff)
            {
                // Deliberately easing off and peeling aside, so leave it alone and
                // let it drift back into the pack.
                targetSpeed = rotateOffSpeed;
            }
            else if (isAnchor)
            {
                // Front of the pack: a steady pace for everyone behind to reference.
                // No rhythm here, so the tip of the pack does not wander.
                targetSpeed = pelotonBodySpeed;
            }
            else
            {
                // Pace plus this rider's breathing, then a gap-closing boost scaled
                // to how far it has drifted behind the rider it is following. The
                // boost only ever speeds a rider up; the resolver still handles
                // "too close" by capping forward movement.
                targetSpeed = BodyPaceFor(npc);

                if (useGapClosing &&
                    riderAhead != null)
                {
                    float gap =
                        riderAhead.RaceDistance - npc.RaceDistance;

                    float closingBoost =
                        Mathf.Clamp(
                            cohesionGain * (gap - cohesionTargetGap),
                            0f,
                            cohesionMaxBoost);

                    targetSpeed += closingBoost;

                    if (closingBoost > 0f)
                    {
                        closingRiderCount++;
                    }

                    if (gap > largestPackGap)
                    {
                        largestPackGap = gap;
                        largestPackGapBehindName = npc.name;
                    }
                }
            }


            npc.SetStrategicOrder(
                isFront
                    ? NPCRacerController.StrategicRole.PelotonFront
                    : NPCRacerController.StrategicRole.PelotonBody,
                targetSpeed,
                false);

            if (isFront)
            {
                pelotonFrontCount++;
            }
            else
            {
                pelotonBodyCount++;
            }
        }

        foreach (NPCRacerController npc in orderedRearToFront)
        {
            if (npc == null ||
                mainPack.Contains(npc))
            {
                continue;
            }

            if (npc.RaceDistance >
                mainPackFrontDistance + detachedAheadGap)
            {
                npc.SetStrategicOrder(
                    NPCRacerController.StrategicRole.PelotonBody,
                    detachedReturnSpeed,
                    false);

                npc.SetPelotonReturnOverride(
                    detachedReturnSpeed,
                    refreshInterval * 2.5f);

                detachedAheadCount++;
                continue;
            }

            if (npc.RaceDistance <
                mainPackRearDistance - droppedBehindGap)
            {
                npc.ClearPelotonReturnOverride();

                npc.SetStrategicOrder(
                    NPCRacerController.StrategicRole.PelotonBody,
                    catchUpSpeed,
                    false);

                droppedBehindCount++;
                continue;
            }

            npc.ClearPelotonReturnOverride();

            npc.SetStrategicOrder(
                NPCRacerController.StrategicRole.PelotonBody,
                BodyPaceFor(npc),
                false);

            pelotonBodyCount++;
        }
    }

    // Normal pack pace plus this rider's personal slow rhythm. Each rider gets a
    // stable random period/phase from its ID, so they breathe out of phase with
    // each other and continuously shuffle. Zero-mean, so the pack holds together.
    private float BodyPaceFor(NPCRacerController npc)
    {
        if (!usePaceRhythm || npc == null || rhythmAmplitude <= 0f)
        {
            return pelotonBodySpeed;
        }

        int id = npc.GetInstanceID();
        float period = Mathf.Lerp(rhythmMinPeriod, rhythmMaxPeriod, Hash01(id));
        float phase = Hash01(id * 7 + 11) * (Mathf.PI * 2f);
        float wave = Mathf.Sin(Time.time * (Mathf.PI * 2f / period) + phase); // -1..1

        return pelotonBodySpeed * (1f + rhythmAmplitude * wave);
    }

    private static float Hash01(int seed)
    {
        float value = Mathf.Sin(seed * 12.9898f) * 43758.5453f;
        return value - Mathf.Floor(value);
    }

    // The rider directly ahead of mainPack[index] in race order. The rotating-off
    // rider is skipped: it has peeled aside on purpose, so riders behind it should
    // close on whoever is genuinely leading rather than pace off a rider that is
    // easing back and changing lanes. Returns null for the front of the pack.
    private NPCRacerController RiderAheadInPack(int index)
    {
        bool rotatingActive =
            rotatingOffRider != null &&
            Time.time < rotatingOffUntil;

        for (int j = index + 1; j < mainPack.Count; j++)
        {
            NPCRacerController candidate = mainPack[j];

            if (candidate == null)
            {
                continue;
            }

            if (rotatingActive &&
                candidate == rotatingOffRider)
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    // The nearest rider ahead of mainPack[index] that is in the same target lane.
    // Same skip rules as RiderAheadInPack. Returns null when this rider is the
    // front of its lane (the caller then falls back to the global rider ahead).
    private NPCRacerController SameLaneRiderAhead(int index, int lane)
    {
        bool rotatingActive =
            rotatingOffRider != null &&
            Time.time < rotatingOffUntil;

        for (int j = index + 1; j < mainPack.Count; j++)
        {
            NPCRacerController candidate = mainPack[j];

            if (candidate == null ||
                candidate.Motor == null)
            {
                continue;
            }

            if (rotatingActive &&
                candidate == rotatingOffRider)
            {
                continue;
            }

            if (candidate.Motor.CurrentTargetLane == lane)
            {
                return candidate;
            }
        }

        return null;
    }

    private void UpdateFrontRotation()
    {
        if (!useFrontRotation ||
            mainPack == null ||
            mainPack.Count == 0)
        {
            currentFrontRider = null;
            currentFrontRiderName = "None";
            rotatingOffRider = null;
            rotatingOffRiderName = "None";
            return;
        }

        List<NPCRacerController> sortedFrontToRear =
            new List<NPCRacerController>();

        for (int i = 0; i < mainPack.Count; i++)
        {
            if (mainPack[i] != null)
            {
                sortedFrontToRear.Add(mainPack[i]);
            }
        }

        sortedFrontToRear.Sort(
            (a, b) => b.RaceDistance.CompareTo(a.RaceDistance));

        if (sortedFrontToRear.Count == 0)
        {
            currentFrontRider = null;
            currentFrontRiderName = "None";
            return;
        }

        if (currentFrontRider == null ||
            !mainPack.Contains(currentFrontRider))
        {
            currentFrontRider =
                sortedFrontToRear[0];

            currentFrontRiderName =
                currentFrontRider.name;

            nextFrontRotationTime =
                Time.time + frontRotationInterval;

            return;
        }

        if (Time.time < nextFrontRotationTime)
        {
            currentFrontRiderName =
                currentFrontRider.name;

            if (rotatingOffRider != null &&
                Time.time < rotatingOffUntil)
            {
                rotatingOffRiderName =
                    rotatingOffRider.name;
            }
            else
            {
                rotatingOffRider = null;
                rotatingOffRiderName = "None";
            }

            return;
        }

        float frontDistance =
            sortedFrontToRear[0].RaceDistance;

        List<NPCRacerController> candidates =
            new List<NPCRacerController>();

        for (int i = 0; i < sortedFrontToRear.Count; i++)
        {
            NPCRacerController candidate =
                sortedFrontToRear[i];

            if (candidate == currentFrontRider)
            {
                continue;
            }

            if (candidate.RaceDistance <
                frontDistance - frontCandidateWindow)
            {
                continue;
            }

            candidates.Add(candidate);
        }

        rotatingOffRider =
            currentFrontRider;

        rotatingOffUntil =
            Time.time + rotateOffDuration;
        if (peelOffOnRotation &&
            rotatingOffRider != null)
        {
            rotatingOffRider.StepAside(
                peelSign,
                rotationPeelLanes);

            peelSign = -peelSign;
        }

        rotatingOffRiderName =
            rotatingOffRider != null
                ? rotatingOffRider.name
                : "None";

        if (candidates.Count > 0)
        {
            int candidateLimit =
                Mathf.Min(
                    frontRotationCandidateCount,
                    candidates.Count);

            int selectedIndex =
                Random.Range(
                    0,
                    candidateLimit);

            currentFrontRider =
                candidates[selectedIndex];
        }
        else
        {
            currentFrontRider =
                sortedFrontToRear[0];
        }

        currentFrontRiderName =
            currentFrontRider != null
                ? currentFrontRider.name
                : "None";

        nextFrontRotationTime =
            Time.time + frontRotationInterval;
    }

}
