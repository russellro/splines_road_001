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

    [Tooltip("Front rider should only be slightly faster than the body or they will become an accidental breakaway.")]
    [SerializeField, Range(0f, 1f)]
    private float pelotonFrontSpeed = 0.53f;

    [Tooltip("Speed used when a rider falls off the back and needs to catch the pack.")]
    [SerializeField, Range(0f, 1f)]
    private float catchUpSpeed = 0.62f;

    [Tooltip("Speed used when an accidental front rider must drift back into the peloton.")]
    [SerializeField, Range(0f, 1f)]
    private float detachedReturnSpeed = 0.45f;

    [Header("Front Rotation")]
    [SerializeField]
    private bool useFrontRotation = true;

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

    [SerializeField] private NPCRacerController currentFrontRider;
    [SerializeField] private string currentFrontRiderName = "None";
    [SerializeField] private NPCRacerController rotatingOffRider;
    [SerializeField] private string rotatingOffRiderName = "None";

    private float nextFrontRotationTime;
    private float rotatingOffUntil;

    [Header("Rear Pack Compression")]
    [SerializeField]
    private bool useRearPackCompression = true;

    [Tooltip("Do not compress using gaps in the first X riders. This protects the front from surging.")]
    [SerializeField, Min(0)]
    private int compressionIgnoreFrontCount = 15;

    [Tooltip("Gap size where the rear group starts gently compressing.")]
    [SerializeField, Min(0f)]
    private float compressionStartGap = 2.5f;

    [Tooltip("Gap size where the rear group uses the full compression speed.")]
    [SerializeField, Min(0.1f)]
    private float compressionFullGap = 10f;

    [Tooltip("Maximum speed used by rear groups to compress gaps. Keep this only slightly above peloton body speed.")]
    [SerializeField, Range(0f, 1f)]
    private float compressionMaxSpeed = 0.57f;

    [Tooltip("How quickly compression speed can change. Lower = smoother, higher = more reactive.")]
    [SerializeField, Min(0f)]
    private float compressionSpeedChangePerSecond = 0.05f;

    [SerializeField] private int compressingRiderCount;
    [SerializeField] private string largestCompressionGapBehindName = "None";
    [SerializeField] private float largestCompressionGap;

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

    private readonly Dictionary<NPCRacerController, float> rearCompressionTargetSpeeds =
        new Dictionary<NPCRacerController, float>();

    private readonly Dictionary<NPCRacerController, float> smoothedRearCompressionSpeeds =
        new Dictionary<NPCRacerController, float>();

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

        UpdateRearPackCompression();
        UpdateFrontRotation();

        for (int i = 0; i < mainPack.Count; i++)
        {
            NPCRacerController npc =
                mainPack[i];

            if (npc == null)
            {
                continue;
            }

            npc.ClearPelotonReturnOverride();

            bool isFront =
                npc == currentFrontRider;

            bool isRotatingOff =
                npc == rotatingOffRider &&
                Time.time < rotatingOffUntil;

            float targetSpeed =
                pelotonBodySpeed;

            if (isFront)
            {
                targetSpeed =
                    pelotonFrontSpeed;
            }
            else if (isRotatingOff)
            {
                targetSpeed =
                    rotateOffSpeed;
            }
            else if (rearCompressionTargetSpeeds.TryGetValue(
                npc,
                out float compressionSpeed))
            {
                targetSpeed =
                    compressionSpeed;
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
                pelotonBodySpeed,
                false);

            pelotonBodyCount++;
        }
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

    private void UpdateRearPackCompression()
    {
        rearCompressionTargetSpeeds.Clear();

        compressingRiderCount = 0;
        largestCompressionGapBehindName = "None";
        largestCompressionGap = 0f;

        if (!useRearPackCompression ||
            mainPack == null ||
            mainPack.Count <= compressionIgnoreFrontCount + 1)
        {
            smoothedRearCompressionSpeeds.Clear();
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

        Dictionary<NPCRacerController, float> desiredSpeeds =
            new Dictionary<NPCRacerController, float>();

        for (int i = compressionIgnoreFrontCount;
            i < sortedFrontToRear.Count;
            i++)
        {
            NPCRacerController riderAhead =
                sortedFrontToRear[i - 1];

            NPCRacerController firstRiderBehindGap =
                sortedFrontToRear[i];

            if (riderAhead == null ||
                firstRiderBehindGap == null)
            {
                continue;
            }

            float gap =
                riderAhead.RaceDistance -
                firstRiderBehindGap.RaceDistance;

            if (gap > largestCompressionGap)
            {
                largestCompressionGap =
                    gap;

                largestCompressionGapBehindName =
                    firstRiderBehindGap.name;
            }

            if (gap <= compressionStartGap)
            {
                continue;
            }

            float gap01 =
                Mathf.InverseLerp(
                    compressionStartGap,
                    compressionFullGap,
                    gap);

            float desiredCompressionSpeed =
                Mathf.Lerp(
                    pelotonBodySpeed,
                    compressionMaxSpeed,
                    gap01);

            desiredCompressionSpeed =
                Mathf.Clamp(
                    desiredCompressionSpeed,
                    pelotonBodySpeed,
                    compressionMaxSpeed);

            // Rear-pack compression: everyone behind this gap gets the same
            // gentle speed increase. This closes the hole as a group instead
            // of launching one rider forward and causing accordion motion.
            for (int j = i; j < sortedFrontToRear.Count; j++)
            {
                NPCRacerController rearGroupRider =
                    sortedFrontToRear[j];

                if (rearGroupRider == null)
                {
                    continue;
                }

                if (!desiredSpeeds.TryGetValue(
                        rearGroupRider,
                        out float existingSpeed) ||
                    desiredCompressionSpeed > existingSpeed)
                {
                    desiredSpeeds[rearGroupRider] =
                        desiredCompressionSpeed;
                }
            }
        }

        HashSet<NPCRacerController> allRidersToUpdate =
            new HashSet<NPCRacerController>();

        foreach (NPCRacerController rider in desiredSpeeds.Keys)
        {
            allRidersToUpdate.Add(rider);
        }

        foreach (NPCRacerController rider in smoothedRearCompressionSpeeds.Keys)
        {
            allRidersToUpdate.Add(rider);
        }

        List<NPCRacerController> ridersToRemove =
            new List<NPCRacerController>();

        float smoothingStep =
            compressionSpeedChangePerSecond *
            refreshInterval;

        foreach (NPCRacerController rider in allRidersToUpdate)
        {
            if (rider == null ||
                !mainPack.Contains(rider))
            {
                ridersToRemove.Add(rider);
                continue;
            }

            float desiredSpeed =
                desiredSpeeds.TryGetValue(
                    rider,
                    out float storedDesiredSpeed)
                    ? storedDesiredSpeed
                    : pelotonBodySpeed;

            float previousSpeed =
                smoothedRearCompressionSpeeds.TryGetValue(
                    rider,
                    out float storedPreviousSpeed)
                    ? storedPreviousSpeed
                    : pelotonBodySpeed;

            float smoothedSpeed =
                Mathf.MoveTowards(
                    previousSpeed,
                    desiredSpeed,
                    smoothingStep);

            if (desiredSpeed <= pelotonBodySpeed &&
                Mathf.Abs(smoothedSpeed - pelotonBodySpeed) < 0.001f)
            {
                ridersToRemove.Add(rider);
                continue;
            }

            smoothedRearCompressionSpeeds[rider] =
                smoothedSpeed;

            if (smoothedSpeed > pelotonBodySpeed + 0.001f)
            {
                rearCompressionTargetSpeeds[rider] =
                    smoothedSpeed;

                compressingRiderCount++;
            }
        }

        for (int i = 0; i < ridersToRemove.Count; i++)
        {
            smoothedRearCompressionSpeeds.Remove(
                ridersToRemove[i]);
        }
    }

}
