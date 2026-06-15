using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ATPBucketHUD : MonoBehaviour
{
    [Header("Player References")]
    [SerializeField] private RacerMotor playerMotor;
    [SerializeField] private RacerEnergy playerEnergy;
    [SerializeField] private RacerAwareness playerAwareness;

    [Header("HUD References")]
    [SerializeField] private TMP_Text wattsText;
    [SerializeField] private TMP_Text gradeText;
    [SerializeField] private RectTransform pixelContainer;
    [SerializeField] private Image pixelPrefab;

    [Header("ATP Pixels")]
    [SerializeField, Min(1)] private int pixelCount = 100;

    [Header("ATP Colors")]
    [SerializeField] private Color emptyColor = new Color(0.15f, 0.15f, 0.15f, 1f);
    [SerializeField] private Color regeneratingColor = Color.green;
    [SerializeField] private Color draftingColor = Color.yellow;
    [SerializeField] private Color drainingColor = Color.red;
    [SerializeField] private Color bonkFlashColor = Color.white;

    [Header("Bonk Warning")]
    [Tooltip("The bucket flashes red and white once ATP reaches this level or lower.")]
    [SerializeField, Min(0f)] private float bonkThreshold = 100f;

    [Tooltip("Seconds between flashes while the rider is in the bonk zone.")]
    [SerializeField, Min(0.05f)] private float flashInterval = 0.15f;

    [Header("Grade Display")]
    [SerializeField, Min(0f)]
    private float gradeDeadZone = 0.2f;

    private readonly List<Image> pixels = new();
    private float previousATP;

    private void Start()
    {
        BuildPixels();

        if (playerEnergy != null)
        {
            previousATP =
                playerEnergy.CurrentATP;
        }
    }

    private void Update()
    {
        if (playerMotor == null ||
            playerEnergy == null)
        {
            return;
        }

        UpdateWattsText();
        UpdateGradeText();
        UpdateBucket();
    }

    private void BuildPixels()
    {
        if (pixelContainer == null ||
            pixelPrefab == null)
        {
            Debug.LogError(
                $"{name}: Assign the pixel container and pixel prefab.");

            return;
        }

        foreach (Transform child in pixelContainer)
        {
            Destroy(
                child.gameObject);
        }

        pixels.Clear();

        for (int i = 0;
            i < pixelCount;
            i++)
        {
            Image pixel =
                Instantiate(
                    pixelPrefab,
                    pixelContainer);

            pixel.name =
                $"ATP Pixel {i + 1:000}";

            pixel.color =
                emptyColor;

            pixels.Add(
                pixel);
        }
    }

    private void UpdateWattsText()
    {
        if (wattsText == null)
        {
            return;
        }

        wattsText.text =
            $"WATTS {playerEnergy.EffectiveWattsPercent:000}%";
    }

    private void UpdateGradeText()
    {
        if (gradeText == null)
        {
            return;
        }

        float grade =
            playerMotor.CurrentSlopePercent;

        if (grade > gradeDeadZone)
        {
            gradeText.text =
                $"GRADE ▲ {grade:0.0}%";

            return;
        }

        if (grade < -gradeDeadZone)
        {
            gradeText.text =
                $"GRADE ▼ {Mathf.Abs(grade):0.0}%";

            return;
        }

        gradeText.text =
            "GRADE — 0.0%";
    }

    private void UpdateBucket()
    {
        float currentATP =
            playerEnergy.CurrentATP;

        float normalizedATP =
            playerEnergy.NormalizedATP;

        float atpChangePerSecond =
            0f;

        if (Time.deltaTime > 0f)
        {
            atpChangePerSecond =
                (currentATP -
                previousATP) /
                Time.deltaTime;
        }

        bool isRegenerating =
            atpChangePerSecond >
            0.01f;

        bool isBonked =
            currentATP <=
            bonkThreshold;

        bool isDrafting =
            playerAwareness != null &&
            playerAwareness.IsDrafting;

        Color activeColor =
            GetActiveColor(
                isRegenerating,
                isDrafting,
                isBonked);

        int filledPixelCount =
            Mathf.RoundToInt(
                normalizedATP *
                pixelCount);

        for (int i = 0;
            i < pixels.Count;
            i++)
        {
            pixels[i].color =
                i < filledPixelCount
                    ? activeColor
                    : emptyColor;
        }

        previousATP =
            currentATP;
    }

    private Color GetActiveColor(
        bool isRegenerating,
        bool isDrafting,
        bool isBonked)
    {
        if (isBonked)
        {
            bool flashOn =
                Mathf.FloorToInt(
                    Time.unscaledTime /
                    flashInterval) %
                2 ==
                0;

            return flashOn
                ? drainingColor
                : bonkFlashColor;
        }

        if (isDrafting)
        {
            return draftingColor;
        }

        if (isRegenerating)
        {
            return regeneratingColor;
        }

        return drainingColor;
    }
}
