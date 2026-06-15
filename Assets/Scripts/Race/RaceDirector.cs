using System.Collections;
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

    private RacerMotor[] racers;
    private RaceState currentState = RaceState.Waiting;
    private float elapsedRaceTime;

    public RaceState CurrentState => currentState;
    public float ElapsedRaceTime => elapsedRaceTime;

    private void Start()
    {
        StartCoroutine(PrepareRace());
    }

    private IEnumerator PrepareRace()
    {
        racers = FindObjectsByType<RacerMotor>(FindObjectsSortMode.None);

        SetRacerMovement(false);

        // Wait until each TrackPath has finished preparing its spline cache.
        yield return new WaitUntil(AreRacerTracksReady);

        // Grid slots are owned by NPCGridSpawner (assigned in its Awake).
        // We only re-snap racers onto those slots now that the track is ready,
        // and reset their progress/energy for a clean start.
        ResetRacersToStartingGrid();

        if (raceManager != null)
        {
            raceManager.RefreshRacers();
        }

        yield return StartCoroutine(RunCountdown());
    }

    private bool AreRacerTracksReady()
    {
        if (racers == null || racers.Length == 0)
        {
            Debug.LogWarning("RaceDirector: No racers were found.");
            return false;
        }

        foreach (RacerMotor racer in racers)
        {
            if (racer == null)
            {
                Debug.LogWarning("RaceDirector: Found a missing racer reference.");
                return false;
            }

            if (racer.Track == null)
            {
                Debug.LogWarning($"RaceDirector: {racer.name} does not have a TrackPath assigned.");
                return false;
            }

            if (racer.Track.Length <= 0f)
            {
                Debug.LogWarning($"RaceDirector: {racer.name}'s track length is not ready.");
                return false;
            }
        }

        return true;
    }

    private void ResetRacersToStartingGrid()
    {
        foreach (RacerMotor racer in racers)
        {
            if (racer == null)
            {
                continue;
            }

            racer.ResetToStartingGrid();

            RacerProgress progress = racer.GetComponent<RacerProgress>();
            if (progress != null)
            {
                progress.ResetProgress();
            }

            RacerEnergy energy = racer.GetComponent<RacerEnergy>();
            if (energy != null)
            {
                energy.ResetEnergy();
            }
        }
    }

    private void Update()
    {
        if (currentState != RaceState.Racing)
        {
            return;
        }

        elapsedRaceTime += Time.deltaTime;

        UpdateTimerText();

        if (playerProgress != null && playerProgress.HasFinished)
        {
            FinishRace();
        }
    }

    private IEnumerator RunCountdown()
    {
        currentState = RaceState.Countdown;

        ShowCenterMessage("3");
        yield return new WaitForSeconds(timeBetweenCountdownNumbers);

        ShowCenterMessage("2");
        yield return new WaitForSeconds(timeBetweenCountdownNumbers);

        ShowCenterMessage("1");
        yield return new WaitForSeconds(timeBetweenCountdownNumbers);

        ShowCenterMessage("GO!");

        currentState = RaceState.Racing;
        SetRacerMovement(true);

        yield return new WaitForSeconds(1f);

        HideCenterMessage();
    }

    private void FinishRace()
    {
        if (currentState == RaceState.Finished)
        {
            return;
        }

        currentState = RaceState.Finished;
        SetRacerMovement(false);

        int finalPosition = 0;

        if (raceManager != null && playerProgress != null)
        {
            finalPosition = raceManager.GetPosition(playerProgress);
        }

        ShowCenterMessage($"FINISH\nPOSITION {finalPosition}");
    }

    private void SetRacerMovement(bool isEnabled)
    {
        if (racers == null)
        {
            return;
        }

        foreach (RacerMotor racer in racers)
        {
            if (racer != null)
            {
                racer.SetMovementEnabled(isEnabled);
            }
        }
    }

    private void UpdateTimerText()
    {
        if (timerText == null)
        {
            return;
        }

        int minutes = Mathf.FloorToInt(elapsedRaceTime / 60f);
        float seconds = elapsedRaceTime % 60f;

        timerText.text = $"TIME {minutes:00}:{seconds:00.0}";
    }

    private void ShowCenterMessage(string message)
    {
        if (centerMessageText == null)
        {
            return;
        }

        centerMessageText.gameObject.SetActive(true);
        centerMessageText.text = message;
    }

    private void HideCenterMessage()
    {
        if (centerMessageText == null)
        {
            return;
        }

        centerMessageText.gameObject.SetActive(false);
    }
}