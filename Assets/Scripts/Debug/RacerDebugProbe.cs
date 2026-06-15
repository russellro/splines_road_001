using System;
using System.Reflection;
using System.Text;
using UnityEngine;

public class RacerDebugProbe : MonoBehaviour
{
    [Header("LIVE DEBUG — DO NOT EDIT THESE VALUES")]
    [SerializeField, TextArea(24, 50)]
    private string debugReadout;

    [SerializeField]
    private float refreshInterval = 0.2f;

    private float nextRefreshTime;

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime =
            Time.unscaledTime + refreshInterval;

        RefreshDebugReadout();
    }

    [ContextMenu("Refresh Debug Readout")]
    private void RefreshDebugReadout()
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine($"OBJECT: {gameObject.name}");
        builder.AppendLine($"POSITION: {transform.position}");
        builder.AppendLine();

        Component motor =
            FindComponentByTypeName("RacerMotor");

        Component energy =
            FindComponentByTypeName("RacerEnergy");

        Component awareness =
            FindComponentByTypeName("RacerAwareness");

        Component npcController =
            FindComponentByTypeName("NPCRacerController");

        Component progress =
            FindComponentByTypeName("RacerProgress");

        AddSection(builder, "RacerMotor");
        AddValue(builder, motor, "Movement Enabled",
            "movementEnabled", "MovementEnabled");

        AddValue(builder, motor, "Current Speed",
            "currentSpeed", "CurrentSpeed");

        AddValue(builder, motor, "Target Speed",
            "targetSpeed", "TargetSpeed");

        AddValue(builder, motor, "Throttle Input",
            "throttleInput", "ThrottleInput");

        AddValue(builder, motor, "Current Lane",
            "currentLane", "CurrentLane");

        AddValue(builder, motor, "Target Lane",
            "targetLane", "CurrentTargetLane",
            "TargetLane");

        AddValue(builder, motor, "Distance Along Track",
            "distanceAlongTrack", "DistanceAlongTrack");

        AddValue(builder, motor, "Total Distance Travelled",
            "totalDistanceTravelled",
            "TotalDistanceTravelled");

        AddValue(builder, motor, "Exhausted Speed",
            "exhaustedSpeed", "ExhaustedSpeed");

        builder.AppendLine();

        AddSection(builder, "RacerEnergy");
        AddValue(builder, energy, "Current ATP",
            "currentATP", "CurrentATP");

        AddValue(builder, energy, "Maximum ATP",
            "maximumATP", "MaximumATP");

        AddValue(builder, energy, "Normalized ATP",
            "normalizedATP", "NormalizedATP");

        builder.AppendLine();

        AddSection(builder, "RacerAwareness");
        AddValue(builder, awareness, "Racer Ahead",
            "racerAhead", "RacerAhead");

        AddValue(builder, awareness,
            "Distance To Racer Ahead",
            "distanceToRacerAhead",
            "DistanceToRacerAhead");

        AddValue(builder, awareness, "Has Racer Ahead",
            "hasRacerAhead", "HasRacerAhead");

        AddValue(builder, awareness, "Is Blocked",
            "isBlocked", "IsBlocked");

        AddValue(builder, awareness, "Is Drafting",
            "isDrafting", "IsDrafting");

        builder.AppendLine();

        AddSection(builder, "NPCRacerController");
        AddValue(builder, npcController, "Current State",
            "currentState", "CurrentState");

        AddValue(builder, npcController, "Cruising Fraction",
            "cruisingSpeedFraction");

        AddValue(builder, npcController, "Drafting Fraction",
            "draftingSpeedFraction");

        AddValue(builder, npcController,
            "Conserving Fraction",
            "conservingSpeedFraction");

        AddValue(builder, npcController, "Passing Fraction",
            "passingSpeedFraction");

        AddValue(builder, npcController, "Sprint Fraction",
            "sprintSpeedFraction");

        AddValue(builder, npcController, "Conserve Below ATP",
            "conserveBelowATP");

        builder.AppendLine();

        AddSection(builder, "RacerProgress");
        AddValue(builder, progress, "Completed Laps",
            "completedLaps", "CompletedLaps");

        AddValue(builder, progress, "Display Lap",
            "displayLap", "DisplayLap");

        AddValue(builder, progress, "Ranking Distance",
            "rankingDistance", "RankingDistance");

        debugReadout = builder.ToString();
    }

    private Component FindComponentByTypeName(
        string typeName)
    {
        foreach (Component component in
                 GetComponentsInParent<Component>(true))
        {
            if (component != null &&
                component.GetType().Name == typeName)
            {
                return component;
            }
        }

        foreach (Component component in
                 GetComponentsInChildren<Component>(true))
        {
            if (component != null &&
                component.GetType().Name == typeName)
            {
                return component;
            }
        }

        return null;
    }

    private void AddSection(
        StringBuilder builder,
        string sectionName)
    {
        builder.AppendLine($"--- {sectionName} ---");
    }

    private void AddValue(
        StringBuilder builder,
        Component component,
        string displayName,
        params string[] possibleMemberNames)
    {
        if (component == null)
        {
            builder.AppendLine($"{displayName}: component not found");
            return;
        }

        foreach (string memberName in possibleMemberNames)
        {
            if (TryReadMember(
                    component,
                    memberName,
                    out object value))
            {
                builder.AppendLine(
                    $"{displayName}: {FormatValue(value)}");

                return;
            }
        }

        builder.AppendLine($"{displayName}: not found");
    }

    private bool TryReadMember(
        Component component,
        string memberName,
        out object value)
    {
        Type currentType = component.GetType();

        while (currentType != null)
        {
            BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly;

            foreach (FieldInfo field in
                     currentType.GetFields(flags))
            {
                if (string.Equals(
                        field.Name,
                        memberName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    value = field.GetValue(component);
                    return true;
                }
            }

            foreach (PropertyInfo property in
                     currentType.GetProperties(flags))
            {
                if (!property.CanRead ||
                    property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (string.Equals(
                        property.Name,
                        memberName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    value = property.GetValue(component);
                    return true;
                }
            }

            currentType = currentType.BaseType;
        }

        value = null;
        return false;
    }

    private string FormatValue(object value)
    {
        if (value == null)
        {
            return "null";
        }

        if (value is float floatValue)
        {
            return floatValue.ToString("0.###");
        }

        if (value is double doubleValue)
        {
            return doubleValue.ToString("0.###");
        }

        if (value is UnityEngine.Object unityObject)
        {
            return unityObject.name;
        }

        return value.ToString();
    }
}