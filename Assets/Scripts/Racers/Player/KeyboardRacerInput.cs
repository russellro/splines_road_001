using UnityEngine;
using UnityEngine.InputSystem;

public class KeyboardRacerInput : MonoBehaviour
{
    [SerializeField] private RacerMotor motor;

    private void Awake()
    {
        if (motor == null)
        {
            motor = GetComponent<RacerMotor>();
        }
    }

    private void Update()
    {
        if (motor == null ||
            Keyboard.current == null)
        {
            return;
        }

        // Q is reserved for the directional push system.
        // PlayerPelotonDirectionalPush handles Q + arrows.
        if (Keyboard.current.qKey.isPressed)
        {
            return;
        }

        // Left/right still change lanes.
        if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
        {
            motor.ChangeLane(-1);
        }

        if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
        {
            motor.ChangeLane(1);
        }

        // IMPORTANT:
        // Do not read up/down here anymore.
        // Up/down now belong to RacerEffortInput for zone changes.
    }
}