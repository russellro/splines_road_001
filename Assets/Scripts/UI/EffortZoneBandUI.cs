using UnityEngine;
using UnityEngine.UI;

public class EffortZoneBandUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RacerEnergy energy;
    [SerializeField] private Image[] zoneSquares = new Image[7];

    [Header("Behavior")]
    [SerializeField] private bool cumulativeFill = true;
    [SerializeField] private bool autoFindPlayerEnergy = true;
    [SerializeField] private bool autoFindZoneSquares = true;

    [Header("Inactive")]
    [SerializeField] private Color inactiveColor = new Color(0.12f, 0.12f, 0.12f, 0.45f);

    [Header("Zone Colors")]
    [SerializeField] private Color zone1Color = new Color(0.20f, 0.80f, 0.20f);
    [SerializeField] private Color zone2Color = new Color(0.35f, 0.90f, 0.35f);
    [SerializeField] private Color zone3Color = new Color(0.70f, 0.90f, 0.25f);
    [SerializeField] private Color zone4Color = new Color(1.00f, 0.85f, 0.20f);
    [SerializeField] private Color zone5Color = new Color(1.00f, 0.60f, 0.10f);
    [SerializeField] private Color zone6Color = new Color(0.95f, 0.20f, 0.15f);
    [SerializeField] private Color zone7Color = new Color(1.00f, 0.00f, 0.60f);

    private Color[] zoneColors;

    private void Awake()
    {
        BuildColorArray();

        if (autoFindZoneSquares)
        {
            FindZoneSquares();
        }
    }

    private void OnEnable()
    {
        if (autoFindPlayerEnergy && energy == null)
        {
            TryFindPlayerEnergy();
        }

        RefreshBand();
    }

    private void Update()
    {
        if (autoFindPlayerEnergy && energy == null)
        {
            TryFindPlayerEnergy();
        }

        if (autoFindZoneSquares && (zoneSquares == null || zoneSquares.Length == 0 || zoneSquares[0] == null))
        {
            FindZoneSquares();
        }

        RefreshBand();
    }

    private void BuildColorArray()
    {
        zoneColors = new Color[]
        {
            zone1Color,
            zone2Color,
            zone3Color,
            zone4Color,
            zone5Color,
            zone6Color,
            zone7Color
        };
    }

    private void FindZoneSquares()
    {
        Image[] foundImages = GetComponentsInChildren<Image>(true);

        // If the EffortZoneBand object itself has an Image component,
        // we do not want to count that as one of the 7 zone squares.
        Image selfImage = GetComponent<Image>();

        int count = 0;
        zoneSquares = new Image[7];

        for (int i = 0; i < foundImages.Length && count < 7; i++)
        {
            if (foundImages[i] == selfImage)
                continue;

            zoneSquares[count] = foundImages[i];
            count++;
        }

        if (count < 7)
        {
            Debug.LogWarning($"{name}: Found only {count} zone square Images. Need 7.");
        }
    }

    private void TryFindPlayerEnergy()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");

        if (player != null)
        {
            energy = player.GetComponent<RacerEnergy>();

            if (energy == null)
            {
                energy = player.GetComponentInChildren<RacerEnergy>();
            }
        }

        if (energy == null)
        {
            // Fallback while prototyping.
            energy = FindFirstObjectByType<RacerEnergy>();
        }

        if (energy == null)
        {
            Debug.LogWarning($"{name}: Could not find RacerEnergy. Check that the player has RacerEnergy and is tagged Player.");
        }
    }

    private void RefreshBand()
    {
        if (energy == null)
            return;

        if (zoneSquares == null || zoneSquares.Length == 0)
            return;

        int currentZone = Mathf.Clamp(
            energy.CurrentZoneIndex,
            0,
            zoneSquares.Length - 1);

        for (int i = 0; i < zoneSquares.Length; i++)
        {
            if (zoneSquares[i] == null)
                continue;

            bool lit = cumulativeFill
                ? i <= currentZone
                : i == currentZone;

            zoneSquares[i].color = lit
                ? zoneColors[Mathf.Min(i, zoneColors.Length - 1)]
                : inactiveColor;
        }
    }
}