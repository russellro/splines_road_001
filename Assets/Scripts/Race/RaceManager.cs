using System.Collections.Generic;
using UnityEngine;

public class RaceManager : MonoBehaviour
{
    [Header("Racers")]
    [SerializeField]
    private List<RacerProgress> racers =
        new List<RacerProgress>();

    private readonly List<RacerProgress> standings =
        new List<RacerProgress>();

    public int RacerCount => standings.Count;

    private void Start()
    {
        RefreshRacers();
        UpdateStandings();
    }

    private void LateUpdate()
    {
        UpdateStandings();
    }

    public void RefreshRacers()
    {
        racers.Clear();

        RacerProgress[] discoveredRacers =
            FindObjectsByType<RacerProgress>(
                FindObjectsSortMode.None);

        racers.AddRange(
            discoveredRacers);

        UpdateStandings();

        Debug.Log(
            $"{name}: Found {racers.Count} racers for standings.");
    }

    public void RegisterRacer(
        RacerProgress racer)
    {
        if (racer == null ||
            racers.Contains(racer))
        {
            return;
        }

        racers.Add(racer);
        UpdateStandings();
    }

    public void UnregisterRacer(
        RacerProgress racer)
    {
        if (racer == null)
        {
            return;
        }

        racers.Remove(racer);
        UpdateStandings();
    }

    public int GetPosition(
        RacerProgress racer)
    {
        int index =
            standings.IndexOf(racer);

        if (index < 0)
        {
            return 0;
        }

        return index + 1;
    }

    public RacerProgress GetRacerInPosition(
        int position)
    {
        int index =
            position - 1;

        if (index < 0 ||
            index >= standings.Count)
        {
            return null;
        }

        return standings[
            index];
    }

    private void UpdateStandings()
    {
        standings.Clear();

        foreach (RacerProgress racer in racers)
        {
            if (racer != null)
            {
                standings.Add(
                    racer);
            }
        }

        standings.Sort(
            CompareRacers);
    }

    private static int CompareRacers(
        RacerProgress first,
        RacerProgress second)
    {
        if (first.HasFinished &&
            second.HasFinished)
        {
            return first.FinishTime.CompareTo(
                second.FinishTime);
        }

        if (first.HasFinished)
        {
            return -1;
        }

        if (second.HasFinished)
        {
            return 1;
        }

        return second.RankingDistance.CompareTo(
            first.RankingDistance);
    }
}
