#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class ScriptUsageAudit
{
    private const string OutputPath = "Assets/ScriptUsageAudit.csv";

    [MenuItem("Tools/Cycling Game/Audit Script Usage")]
    public static void AuditScriptUsage()
    {
        string[] monoScriptGuids =
            AssetDatabase.FindAssets(
                "t:MonoScript",
                new[] { "Assets" });

        List<string> scriptPaths =
            monoScriptGuids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path =>
                    path.EndsWith(
                        ".cs",
                        StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .OrderBy(path => path)
                .ToList();

        string[] prefabGuids =
            AssetDatabase.FindAssets(
                "t:Prefab",
                new[] { "Assets" });

        string[] sceneGuids =
            AssetDatabase.FindAssets(
                "t:Scene",
                new[] { "Assets" });

        List<string> inspectableAssets =
            prefabGuids
                .Concat(sceneGuids)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .OrderBy(path => path)
                .ToList();

        Dictionary<string, List<string>> usageByScript =
            new Dictionary<string, List<string>>();

        foreach (string scriptPath in scriptPaths)
        {
            usageByScript[scriptPath] =
                new List<string>();
        }

        foreach (string assetPath in inspectableAssets)
        {
            string[] dependencies =
                AssetDatabase.GetDependencies(
                    assetPath,
                    true);

            foreach (string dependency in dependencies)
            {
                if (usageByScript.TryGetValue(
                    dependency,
                    out List<string> usages))
                {
                    usages.Add(
                        assetPath);
                }
            }
        }

        StringBuilder csv =
            new StringBuilder();

        csv.AppendLine(
            "Script Path,Class Name,Prefab or Scene Usage Count,Referenced By Prefabs or Scenes");

        foreach (string scriptPath in scriptPaths)
        {
            MonoScript monoScript =
                AssetDatabase.LoadAssetAtPath<MonoScript>(
                    scriptPath);

            string className =
                monoScript != null &&
                monoScript.GetClass() != null
                    ? monoScript.GetClass().Name
                    : "(no attachable class or filename mismatch)";

            List<string> usages =
                usageByScript[scriptPath];

            csv.Append(
                Csv(scriptPath));

            csv.Append(",");

            csv.Append(
                Csv(className));

            csv.Append(",");

            csv.Append(
                usages.Count);

            csv.Append(",");

            csv.AppendLine(
                Csv(
                    string.Join(
                        " | ",
                        usages)));
        }

        File.WriteAllText(
            OutputPath,
            csv.ToString());

        AssetDatabase.Refresh();

        Debug.Log(
            $"Script usage audit complete. Open {OutputPath}. " +
            "A count of zero does not automatically mean a script is safe to delete: " +
            "static classes, editor tools, and classes used only from code can legitimately show zero.");

        UnityEngine.Object report =
            AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                OutputPath);

        Selection.activeObject =
            report;

        EditorGUIUtility.PingObject(
            report);
    }

    private static string Csv(string value)
    {
        if (value == null)
        {
            return "\"\"";
        }

        return "\"" +
            value.Replace(
                "\"",
                "\"\"") +
            "\"";
    }
}
#endif
