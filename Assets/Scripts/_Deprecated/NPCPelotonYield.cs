using UnityEngine;

public class NPCPelotonYield : MonoBehaviour
{
    [Header("Required References")]
    [SerializeField] private RacerMotor motor;
    [SerializeField] private RacerAwareness awareness;

    [Header("Yield Timing")]
    [Tooltip("Minimum time an NPC remains displaced before trying to return.")]
    [SerializeField, Min(0f)] private float minimumHoldDuration = 0.8f;

    [Tooltip("Random additional delay so NPCs do not return simultaneously.")]
    [SerializeField, Min(0f)] private float additionalRandomHoldDuration = 1.2f;

    [Tooltip("Delay between attempts to move back toward the original lane.")]
    [SerializeField, Min(0.05f)] private float returnAttemptInterval = 0.4f;

    [Header("Debug")]
    [SerializeField] private bool isYielding;
    [SerializeField] private int originalLane;
    [SerializeField] private int currentReturnTargetLane;

    private float returnAfterTime;
    private float nextReturnAttemptTime;

    private void Awake()
    {
        if (motor == null) motor = GetComponent<RacerMotor>();
        if (awareness == null) awareness = GetComponent<RacerAwareness>();
    }

    private void Update()
    {
        if (!isYielding || motor == null || Time.time < returnAfterTime || Time.time < nextReturnAttemptTime)
        {
            return;
        }

        TryReturnTowardOriginalLane();
    }

    public void BeginYield(int direction)
    {
        if (motor == null || direction == 0)
        {
            return;
        }

        if (!isYielding)
        {
            originalLane = motor.CurrentTargetLane;
        }

        bool moved = motor.ForceLaneShift(direction);

        if (!moved)
        {
            return;
        }

        isYielding = true;

        float randomDelay = Random.Range(0f, additionalRandomHoldDuration);
        returnAfterTime = Time.time + minimumHoldDuration + randomDelay;
        nextReturnAttemptTime = returnAfterTime;
    }

    private void TryReturnTowardOriginalLane()
    {
        int currentLane = motor.CurrentTargetLane;

        if (currentLane == originalLane)
        {
            FinishYield();
            return;
        }

        int directionTowardOriginalLane = originalLane > currentLane ? 1 : -1;
        int desiredLane = currentLane + directionTowardOriginalLane;

        currentReturnTargetLane = desiredLane;
        nextReturnAttemptTime = Time.time + returnAttemptInterval;

        if (awareness != null && !awareness.CanEnterLane(desiredLane))
        {
            return;
        }

        motor.SetLane(desiredLane);

        if (motor.CurrentTargetLane == originalLane)
        {
            FinishYield();
        }
    }

    private void FinishYield()
    {
        isYielding = false;
        currentReturnTargetLane = originalLane;
    }
}