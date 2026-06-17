using UnityEngine;
using UnityEngine.InputSystem;

// Selects the active zone for RacerEnergy.
// Up / Down arrows shift one zone at a time across zones 1..6.
// Spacebar is held for sprint zone 7.

public class RacerEffortInput : MonoBehaviour
{
    [Header("Arrow-selectable zones")]
    [Tooltip("How many zones the arrows can reach (zones 1..N). Zone 7, the sprint, sits above this and is reached only with the sprint key.")]
    [SerializeField, Min(1)]
    private int arrowZoneCount = 6;

    [Tooltip("Zone the rider starts in (0-based). 1 = Endurance.")]
    [SerializeField, Min(0)]
    private int startingZoneIndex = 1;

    [Header("Sprint")]
    [Tooltip("Zero-based index of the sprint zone. With 7 zones this is 6 (zone 7).")]
    [SerializeField, Min(0)]
    private int sprintZoneIndex = 6;

    private int selectedZoneIndex;

    public int SelectedZoneIndex => selectedZoneIndex;
    public bool IsSprinting { get; private set; }

    public int CurrentZoneIndex =>
        IsSprinting ? sprintZoneIndex : selectedZoneIndex;

    private void Awake()
    {
        selectedZoneIndex =
            Mathf.Clamp(
                startingZoneIndex,
                0,
                Mathf.Max(0, arrowZoneCount - 1));
    }

    private void Update()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        if (Keyboard.current.upArrowKey.wasPressedThisFrame)
        {
            ShiftUp();
        }

        if (Keyboard.current.downArrowKey.wasPressedThisFrame)
        {
            ShiftDown();
        }

        IsSprinting = Keyboard.current.spaceKey.isPressed;

        Debug.Log($"selected {selectedZoneIndex}, sprinting {IsSprinting}, sending {CurrentZoneIndex}");
    }

    public void ShiftUp()
    {
        selectedZoneIndex =
            Mathf.Min(
                selectedZoneIndex + 1,
                Mathf.Max(0, arrowZoneCount - 1));
    }

    public void ShiftDown()
    {
        selectedZoneIndex =
            Mathf.Max(
                selectedZoneIndex - 1,
                0);
    }
}