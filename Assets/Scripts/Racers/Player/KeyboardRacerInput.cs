using UnityEngine;
using UnityEngine.InputSystem;

public class KeyboardRacerInput : MonoBehaviour
{
    [SerializeField] private RacerMotor motor;

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
        if (motor == null ||
            Keyboard.current == null)
        {
            return;
        }

        bool qIsHeld =
            Keyboard.current.qKey.isPressed;

        // Q is reserved as the directional-push modifier.
        // The push component handles Q + arrow-key combinations.
        if (qIsHeld)
        {
            motor.SetThrottle(0f);
            return;
        }

        float wattsAdjustment = 0f;

        if (Keyboard.current.upArrowKey.isPressed)
        {
            wattsAdjustment += 1f;
        }

        if (Keyboard.current.downArrowKey.isPressed)
        {
            wattsAdjustment -= 1f;
        }

        motor.SetThrottle(
            wattsAdjustment);

        if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
        {
            motor.ChangeLane(-1);
        }

        if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
        {
            motor.ChangeLane(1);
        }
    }
}
