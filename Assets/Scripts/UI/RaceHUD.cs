using TMPro;
using UnityEngine;

public class RaceHUD : MonoBehaviour
{
    [Header("Race References")]
    [SerializeField]
    private RacerProgress playerProgress;

    [SerializeField]
    private RaceManager raceManager;

    [Header("Text")]
    [SerializeField]
    private TMP_Text lapText;

    [SerializeField]
    private TMP_Text positionText;

    private void Awake()
    {
        ResolveRaceManager();
    }

    private void ResolveRaceManager()
    {
        RaceManager[] managers =
            FindObjectsByType<RaceManager>(
                FindObjectsSortMode.None);

        if (managers.Length == 1)
        {
            raceManager =
                managers[0];

            return;
        }

        if (managers.Length == 0)
        {
            Debug.LogError(
                $"{name}: RaceHUD could not find a RaceManager.");

            return;
        }

        Debug.LogError(
            $"{name}: RaceHUD found {managers.Length} RaceManager objects. " +
            "Keep only one active RaceManager in the scene.");
    }

    private void Update()
    {
        UpdateLapText();
        UpdatePositionText();
    }

    private void UpdateLapText()
    {
        if (playerProgress == null ||
            lapText == null)
        {
            return;
        }

        if (playerProgress.HasFinished)
        {
            lapText.text =
                "FINISH";

            return;
        }

        lapText.text =
            $"LAP {playerProgress.DisplayLap}" +
            $" / {playerProgress.TotalLaps}";
    }

    private void UpdatePositionText()
    {
        if (playerProgress == null ||
            raceManager == null ||
            positionText == null)
        {
            return;
        }

        int position =
            raceManager.GetPosition(
                playerProgress);

        positionText.text =
            $"POSITION {position}" +
            $" / {raceManager.RacerCount}";
    }
}
