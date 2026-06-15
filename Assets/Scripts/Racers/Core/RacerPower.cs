using UnityEngine;

public class RacerPower : MonoBehaviour
{
    [Header("Watts")]
    [Tooltip("Current rider effort from 0 to 100 percent.")]
    [SerializeField, Range(0f, 100f)]
    private float wattsPercent = 55f;

    [Tooltip("How quickly the rider can increase effort.")]
    [SerializeField, Min(0f)]
    private float wattsIncreasePerSecond = 30f;

    [Tooltip("How quickly the rider can reduce effort.")]
    [SerializeField, Min(0f)]
    private float wattsDecreasePerSecond = 40f;

    public float WattsPercent => wattsPercent;
    public float NormalizedWatts => wattsPercent / 100f;

    public void AdjustWatts(float input)
    {
        input = Mathf.Clamp(input, -1f, 1f);

        if (input > 0f)
        {
            wattsPercent = Mathf.MoveTowards(
                wattsPercent,
                100f,
                wattsIncreasePerSecond *
                input *
                Time.deltaTime);

            return;
        }

        if (input < 0f)
        {
            wattsPercent = Mathf.MoveTowards(
                wattsPercent,
                0f,
                wattsDecreasePerSecond *
                -input *
                Time.deltaTime);
        }
    }

    public void IncreaseWatts()
    {
        AdjustWatts(1f);
    }

    public void DecreaseWatts()
    {
        AdjustWatts(-1f);
    }

    public void SetWattsPercent(float newWattsPercent)
    {
        wattsPercent = Mathf.Clamp(
            newWattsPercent,
            0f,
            100f);
    }

    public void EnsureMinimumWattsPercent(float minimumWattsPercent)
    {
        wattsPercent = Mathf.Max(
            wattsPercent,
            Mathf.Clamp(
                minimumWattsPercent,
                0f,
                100f));
    }
}
