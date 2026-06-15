using System;
using UnityEngine;

public class RacerProgress : MonoBehaviour
{
    [Header("Required Reference")]
    [SerializeField] private RacerMotor motor;

    [Header("Race Settings")]
    [SerializeField, Min(1)] private int totalLaps = 3;

    [Header("Optional Racer Information")]
    [SerializeField] private string racerName = "Racer";

    private bool hasFinished;
    private float finishTime;

    public event Action<RacerProgress> Finished;

    public string RacerName => racerName;
    public int TotalLaps => totalLaps;
    public bool HasFinished => hasFinished;
    public float FinishTime => finishTime;

    public float TotalDistanceTravelled
    {
        get
        {
            if (motor == null)
            {
                return 0f;
            }

            return motor.TotalDistanceTravelled;
        }
    }

    public float RaceDistanceFromStart
    {
        get
        {
            if (motor == null)
            {
                return 0f;
            }

            return Mathf.Max(
                0f,
                motor.RaceDistanceFromStart);
        }
    }

    public float DistanceAlongCurrentLap
    {
        get
        {
            if (motor == null ||
                motor.Track == null ||
                motor.Track.Length <= 0f)
            {
                return 0f;
            }

            return Mathf.Repeat(
                RaceDistanceFromStart,
                motor.Track.Length);
        }
    }

    public int CompletedLaps
    {
        get
        {
            if (motor == null ||
                motor.Track == null ||
                motor.Track.Length <= 0f)
            {
                return 0;
            }

            int completedLaps =
                Mathf.FloorToInt(
                    RaceDistanceFromStart /
                    motor.Track.Length);

            return Mathf.Clamp(
                completedLaps,
                0,
                totalLaps);
        }
    }

    public int DisplayLap
    {
        get
        {
            if (hasFinished)
            {
                return totalLaps;
            }

            return Mathf.Min(
                CompletedLaps + 1,
                totalLaps);
        }
    }

    public float RankingDistance
    {
        get
        {
            if (motor == null ||
                motor.Track == null)
            {
                return 0f;
            }

            float finishingDistance =
                totalLaps *
                motor.Track.Length;

            return Mathf.Min(
                motor.RaceDistanceFromStart,
                finishingDistance);
        }
    }

    private void Awake()
    {
        if (motor == null)
        {
            motor =
                GetComponent<RacerMotor>();
        }
    }

    private void Update()
    {
        CheckForFinish();
    }

    public void ResetProgress()
    {
        hasFinished =
            false;

        finishTime =
            0f;
    }

    private void CheckForFinish()
    {
        if (hasFinished)
        {
            return;
        }

        if (CompletedLaps <
            totalLaps)
        {
            return;
        }

        hasFinished =
            true;

        finishTime =
            Time.timeSinceLevelLoad;

        Finished?.Invoke(
            this);
    }
}
