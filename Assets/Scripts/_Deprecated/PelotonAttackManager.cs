using System.Collections.Generic;
using UnityEngine;

public class PelotonAttackManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PelotonManager pelotonManager;

    [Header("Attack Timing")]
    [Tooltip("Minimum seconds between attack attempts.")]
    [SerializeField, Min(1f)] private float minimumSecondsBetweenAttacks = 12f;

    [Tooltip("Maximum seconds between attack attempts.")]
    [SerializeField, Min(1f)] private float maximumSecondsBetweenAttacks = 22f;

    [Tooltip("How long the initiating rider remains committed to the attack.")]
    [SerializeField, Min(1f)] private float attackDuration = 7f;

    [Header("Followers")]
    [Tooltip("How far behind the attacker a rider can be and still react to the attack.")]
    [SerializeField, Min(1f)] private float followerSearchDistance = 14f;

    [Tooltip("Maximum number of riders that can follow one attack.")]
    [SerializeField, Min(0)] private int maximumFollowers = 4;

    [Tooltip("Chance that an eligible nearby rider follows the attack.")]
    [SerializeField, Range(0f, 1f)] private float followerChance = 0.35f;

    [Tooltip("Followers remain committed slightly longer than the initial attacker.")]
    [SerializeField, Min(0f)] private float followerExtraDuration = 2f;

    [Header("Debug")]
    [SerializeField] private string currentAttackerName = "None";

    private float nextAttackTime;
    private float attackEndsAtTime;

    private void Start()
    {
        ScheduleNextAttack();
    }

    private void Update()
    {
        if (Time.time < nextAttackTime || Time.time < attackEndsAtTime)
        {
            return;
        }

        TryStartAttack();
        ScheduleNextAttack();
    }

    private void TryStartAttack()
    {
        NPCRacerController[] npcs = FindObjectsByType<NPCRacerController>(FindObjectsSortMode.None);
        List<NPCRacerController> eligibleAttackers = new();

        foreach (NPCRacerController npc in npcs)
        {
            if (npc == null || !npc.CanStartAttack)
            {
                continue;
            }

            if (pelotonManager != null && !pelotonManager.IsInLocalPack(npc.Motor))
            {
                continue;
            }

            eligibleAttackers.Add(npc);
        }

        if (eligibleAttackers.Count == 0)
        {
            currentAttackerName = "None";
            return;
        }

        NPCRacerController attacker = eligibleAttackers[Random.Range(0, eligibleAttackers.Count)];

        attacker.BeginAttack(attackDuration);
        currentAttackerName = attacker.name;
        attackEndsAtTime = Time.time + attackDuration;

        RecruitFollowers(attacker, npcs);
    }

    private void RecruitFollowers(NPCRacerController attacker, NPCRacerController[] npcs)
    {
        if (attacker == null || attacker.Motor == null || attacker.Motor.Track == null)
        {
            return;
        }

        int followerCount = 0;

        foreach (NPCRacerController npc in npcs)
        {
            if (npc == null || npc == attacker || !npc.CanFollowAttack)
            {
                continue;
            }

            float distanceBehindAttacker =
                attacker.Motor.UnwrappedTrackDistance -
                npc.Motor.UnwrappedTrackDistance;

            if (distanceBehindAttacker <= 0f || distanceBehindAttacker > followerSearchDistance)
            {
                continue;
            }

            if (Random.value > followerChance)
            {
                continue;
            }

            npc.BeginFollowAttack(attackDuration + followerExtraDuration);
            followerCount++;

            if (followerCount >= maximumFollowers)
            {
                break;
            }
        }
    }

    private void ScheduleNextAttack()
    {
        nextAttackTime = Time.time + Random.Range(minimumSecondsBetweenAttacks, maximumSecondsBetweenAttacks);
    }
}