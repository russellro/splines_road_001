using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class RaceDirector : MonoBehaviour
{
    public enum RaceState
    {
        Waiting,
        Countdown,
        Racing,
        Finished
    }

    [Header("Race References")]
    [SerializeField] private RacerProgress playerProgress;
    [SerializeField] private RaceManager raceManager;

    [Header("HUD")]
    [SerializeField] private TMP_Text centerMessageText;
    [SerializeField] private TMP_Text timerText;

    [Header("Countdown")]
    [SerializeField, Min(0f)] private float timeBetweenCountdownNumbers = 1f;

    [Header("Starting Grid")]
    [Tooltip("Distance around the spline where the starting line is located. Leave at 0 if the start line is at the beginning of the spline.")]
    [SerializeField, Min(0f)] private float startingLineDistance = 0f;

    [Tooltip("How many unique lane slots are used in each row. This must not be greater than the number of usable lanes on the track.")]
    [SerializeField, Min(1)] private int racersPerRow = 4;

    [Tooltip("The first lane index used by the grid. Use 0 when the usable lanes begin with lane 0.")]
    [SerializeField, Min(0)] private int firstGridLane = 0;

    [Tooltip("Distance behind the row ahead. Increase this if rows overlap.")]
    [SerializeField, Min(0.1f)] private float rowSpacing = 2.5f;

    [Tooltip("Distance behind the starting line used by the first row.")]
    [SerializeField, Min(0f)] private float firstRowOffset = 0f;

    private RacerMotor[] racers;
    private RaceState currentState = RaceState.Waiting;
    private float elapsedRaceTime;

    public RaceState CurrentState => currentState;
    public float ElapsedRaceTime => elapsedRaceTime;

    private readonly struct GridSlot
    {
        public GridSlot(int lane, float offset)
        {
            Lane = lane;
            Offset = offset;
        }

        public int Lane { get; }
        public float Offset { get; }
    }

    private void Start()
    {
        StartCoroutine(PrepareRace());
    }

    private IEnumerator PrepareRace()
    {
        racers =
            FindObjectsByType<RacerMotor>(
                FindObjectsSortMode.None);

        SetRacerMovement(false);

        // Wait until each TrackPath has finished preparing its spline cache.
        yield return new WaitUntil(AreRacerTracksReady);

        AssignUniqueRandomStartingGridSlots();
        ResetRacersToStartingGrid();

        if (raceManager != null)
        {
            raceManager.RefreshRacers();
        }

        yield return StartCoroutine(
            RunCountdown());
    }

    private bool AreRacerTracksReady()
    {
        if (racers == null ||
            racers.Length == 0)
        {
            Debug.LogWarning(
                "RaceDirector: No racers were found.");

            return false;
        }

        foreach (RacerMotor racer in racers)
        {
            if (racer == null)
            {
                Debug.LogWarning(
                    "RaceDirector: Found a missing racer reference.");

                return false;
            }

            if (racer.Track == null)
            {
                Debug.LogWarning(
                    $"RaceDirector: {racer.name} does not have a TrackPath assigned.");

                return false;
            }

            if (racer.Track.Length <= 0f)
            {
                Debug.LogWarning(
                    $"RaceDirector: {racer.name}'s track length is not ready.");

                return false;
            }
        }

        return true;
    }

    private void AssignUniqueRandomStartingGridSlots()
    {
        if (racers == null ||
            racers.Length == 0)
        {
            return;
        }

        int requiredRows =
            Mathf.CeilToInt(
                racers.Length /
                (float)racersPerRow);

        List<GridSlot> slots =
            new List<GridSlot>(
                requiredRows *
                racersPerRow);

        for (int row = 0;
            row < requiredRows;
            row++)
        {
            float rowOffset =
                firstRowOffset +
                row *
                rowSpacing;

            for (int column = 0;
                column < racersPerRow;
                column++)
            {
                int lane =
                    firstGridLane +
                    column;

                slots.Add(
                    new GridSlot(
                        lane,
                        rowOffset));
            }
        }

        Shuffle(
            slots);

        for (int i = 0;
            i < racers.Length;
            i++)
        {
            RacerMotor racer =
                racers[i];

            if (racer == null)
            {
                continue;
            }

            GridSlot slot =
                slots[i];

            racer.ConfigureStartingGrid(
                racer.Track,
                slot.Lane,
                startingLineDistance,
                slot.Offset);

            RacerProgress progress =
                racer.GetComponent<RacerProgress>();

            if (progress != null)
            {
                progress.ResetProgress();
            }

            RacerEnergy energy =
                racer.GetComponent<RacerEnergy>();

            if (energy != null)
            {
                energy.ResetEnergy();
            }
        }
    }

    private static void Shuffle(
        List<GridSlot> slots)
    {
        for (int i = slots.Count - 1;
            i > 0;
            i--)
        {
            int randomIndex =
                Random.Range(
                    0,
                    i + 1);

            GridSlot temporary =
                slots[i];

            slots[i] =
                slots[randomIndex];

            slots[randomIndex] =
                temporary;
        }
    }

    private void ResetRacersToStartingGrid()
    {
        foreach (RacerMotor racer in racers)
        {
            if (racer != null)
            {
                racer.ResetToStartingGrid();
            }
        }
    }

    private void Update()
    {
        if (currentState !=
            RaceState.Racing)
        {
            return;
        }

        elapsedRaceTime +=
            Time.deltaTime;

        UpdateTimerText();

        if (playerProgress != null &&
            playerProgress.HasFinished)
        {
            FinishRace();
        }
    }

    private IEnumerator RunCountdown()
    {
        currentState =
            RaceState.Countdown;

        ShowCenterMessage("3");
        yield return new WaitForSeconds(
            timeBetweenCountdownNumbers);

        ShowCenterMessage("2");
        yield return new WaitForSeconds(
            timeBetweenCountdownNumbers);

        ShowCenterMessage("1");
        yield return new WaitForSeconds(
            timeBetweenCountdownNumbers);

        ShowCenterMessage("GO!");

        currentState =
            RaceState.Racing;

        SetRacerMovement(true);

        yield return new WaitForSeconds(
            1f);

        HideCenterMessage();
    }

    private void FinishRace()
    {
        if (currentState ==
            RaceState.Finished)
        {
            return;
        }

        currentState =
            RaceState.Finished;

        SetRacerMovement(false);

        int finalPosition =
            0;

        if (raceManager != null &&
            playerProgress != null)
        {
            finalPosition =
                raceManager.GetPosition(
                    playerProgress);
        }

        ShowCenterMessage(
            $"FINISH\nPOSITION {finalPosition}");
    }

    private void SetRacerMovement(
        bool isEnabled)
    {
        if (racers == null)
        {
            return;
        }

        foreach (RacerMotor racer in racers)
        {
            if (racer != null)
            {
                racer.SetMovementEnabled(
                    isEnabled);
            }
        }
    }

    private void UpdateTimerText()
    {
        if (timerText == null)
        {
            return;
        }

        int minutes =
            Mathf.FloorToInt(
                elapsedRaceTime /
                60f);

        float seconds =
            elapsedRaceTime %
            60f;

        timerText.text =
            $"TIME {minutes:00}:{seconds:00.0}";
    }

    private void ShowCenterMessage(
        string message)
    {
        if (centerMessageText == null)
        {
            return;
        }

        centerMessageText.gameObject.SetActive(
            true);

        centerMessageText.text =
            message;
    }

    private void HideCenterMessage()
    {
        if (centerMessageText == null)
        {
            return;
        }

        centerMessageText.gameObject.SetActive(
            false);
    }
}
