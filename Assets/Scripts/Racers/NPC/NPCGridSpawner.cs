using UnityEngine;

public class NPCGridSpawner : MonoBehaviour
{
    [Header("Required References")]
    [SerializeField] private RacerMotor playerMotor;
    [SerializeField] private RacerMotor npcRacerPrefab;
    [SerializeField] private TrackPath track;
    [SerializeField] private RaceManager raceManager;

    [Header("Race Size")]
    [Tooltip("Includes the player. A value of 20 creates one player and nineteen NPC racers.")]
    [SerializeField, Min(2)] private int totalRacers = 20;

    [Header("Starting Grid")]
    [SerializeField, Min(1)] private int racersPerRow = 4;
    [SerializeField, Min(0f)] private float startingLineDistance = 0.1f;

    [Tooltip("Distance between each row of racers.")]
    [SerializeField, Min(0.1f)] private float rowSpacing = 4f;

    [Header("Player Starting Position")]
    [Tooltip("Earliest part of the peloton where the player may spawn. 0.25 means the player will not start in the front 25 percent.")]
    [SerializeField, Range(0f, 0.9f)] private float minimumPlayerGridFraction = 0.25f;

    [Tooltip("Latest part of the peloton where the player may spawn. 0.75 means the player will not start in the final 25 percent.")]
    [SerializeField, Range(0.1f, 1f)] private float maximumPlayerGridFraction = 0.75f;

    [Header("Organic Grid Variation")]
    [Tooltip("Random front-to-back variation added to starting positions.")]
    [SerializeField, Min(0f)] private float gridOffsetJitter = 0.8f;

    [Tooltip("Small sideways variation within a lane. Keep this much smaller than lane spacing.")]
    [SerializeField, Min(0f)] private float laneBiasRange = 0.25f;

    [Header("Debug")]
    [SerializeField] private int playerGridSlot;
    [SerializeField] private int playerStartingLane;
    [SerializeField] private int playerStartingRow;

    private void Awake()
    {
        ResolveRaceManager();
        ConfigurePlayerStartingGrid();
        SpawnNPCs();
        RefreshStandings();
    }

    private void ResolveRaceManager()
    {
        if (raceManager == null)
        {
            raceManager =
                FindFirstObjectByType<RaceManager>();
        }

        if (raceManager == null)
        {
            Debug.LogError(
                $"{name}: No RaceManager was found in the scene.");
        }
    }

    private void ConfigurePlayerStartingGrid()
    {
        if (playerMotor == null)
        {
            Debug.LogError(
                $"{name}: Player Motor has not been assigned.");

            return;
        }

        if (track == null)
        {
            Debug.LogError(
                $"{name}: TrackPath has not been assigned.");

            return;
        }

        int earliestSlot =
            Mathf.FloorToInt(
                totalRacers *
                minimumPlayerGridFraction);

        int latestSlot =
            Mathf.CeilToInt(
                totalRacers *
                maximumPlayerGridFraction);

        earliestSlot =
            Mathf.Clamp(
                earliestSlot,
                0,
                totalRacers - 1);

        latestSlot =
            Mathf.Clamp(
                latestSlot,
                earliestSlot + 1,
                totalRacers);

        playerGridSlot =
            Random.Range(
                earliestSlot,
                latestSlot);

        GetGridPosition(
            playerGridSlot,
            out playerStartingLane,
            out playerStartingRow,
            out float playerGridOffset);

        float playerLaneBias =
            Random.Range(
                -laneBiasRange,
                laneBiasRange);

        playerMotor.ConfigureStartingGrid(
            track,
            playerStartingLane,
            startingLineDistance,
            playerGridOffset);

        playerMotor.SetLaneBias(
            playerLaneBias);

        RegisterMotor(
            playerMotor);

        Debug.Log(
            $"{name}: Player assigned to grid slot {playerGridSlot}, " +
            $"row {playerStartingRow}, lane {playerStartingLane}.");
    }

    private void SpawnNPCs()
    {
        if (npcRacerPrefab == null)
        {
            Debug.LogError(
                $"{name}: NPC racer prefab has not been assigned.");

            return;
        }

        if (track == null)
        {
            Debug.LogError(
                $"{name}: TrackPath has not been assigned.");

            return;
        }

        int npcNumber =
            1;

        for (int gridSlot = 0;
            gridSlot < totalRacers;
            gridSlot++)
        {
            if (gridSlot ==
                playerGridSlot)
            {
                continue;
            }

            GetGridPosition(
                gridSlot,
                out int lane,
                out int row,
                out float gridOffset);

            float laneBias =
                Random.Range(
                    -laneBiasRange,
                    laneBiasRange);

            RacerMotor npc =
                Instantiate(
                    npcRacerPrefab,
                    transform);

            npc.name =
                $"NPC Racer {npcNumber:000}";

            npc.ConfigureStartingGrid(
                track,
                lane,
                startingLineDistance,
                gridOffset);

            npc.SetLaneBias(
                laneBias);

            RegisterMotor(
                npc);

            npcNumber++;
        }
    }

    private void RegisterMotor(
        RacerMotor motor)
    {
        if (raceManager == null ||
            motor == null)
        {
            return;
        }

        RacerProgress progress =
            motor.GetComponent<RacerProgress>();

        if (progress == null)
        {
            Debug.LogError(
                $"{motor.name}: RacerProgress is missing.");

            return;
        }

        raceManager.RegisterRacer(
            progress);
    }

    private void RefreshStandings()
    {
        if (raceManager != null)
        {
            raceManager.RefreshRacers();
        }
    }

    private void GetGridPosition(
        int gridSlot,
        out int lane,
        out int row,
        out float gridOffset)
    {
        int laneCount =
            track.LaneCount;

        row =
            gridSlot /
            racersPerRow;

        int positionInRow =
            gridSlot %
            racersPerRow;

        lane =
            GetCenterOutLane(
                positionInRow,
                laneCount);

        gridOffset =
            Mathf.Max(
                0f,
                row *
                rowSpacing +
                Random.Range(
                    -gridOffsetJitter,
                    gridOffsetJitter));
    }

    private int GetCenterOutLane(
        int positionInRow,
        int laneCount)
    {
        int centerLane =
            laneCount /
            2;

        if (positionInRow == 0)
        {
            return centerLane;
        }

        int distanceFromCenter =
            (positionInRow + 1) /
            2;

        if (positionInRow % 2 == 1)
        {
            return centerLane -
                distanceFromCenter;
        }

        return centerLane +
            distanceFromCenter;
    }
}
