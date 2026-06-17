using UnityEngine;

/// <summary>
/// Gives this rider its own jersey colour without affecting any other rider.
///
/// It scans every renderer beneath this object, finds only the material slots
/// that use the shared team-colour material, and recolours just those slots
/// per-instance using a MaterialPropertyBlock.
///
/// Because it writes a property block (not the material asset), other riders are
/// untouched and no material clones are created, so GPU batching stays intact.
///
/// Put this on the racer root (Player Racer Prefab / NPC Racer Prefab) — that is
/// the level where the player and NPCs are allowed to differ, even though they
/// share one cyclist prefab underneath.
/// </summary>
public class RiderColor : MonoBehaviour
{
    [Tooltip("The shared jersey material to look for (assign M_TeamColor). " +
             "Any renderer slot using this exact material gets recoloured; " +
             "all other slots — skin, metal, tyres — are left alone.")]
    [SerializeField] private Material teamColorMaterial;

    [Tooltip("This rider's jersey colour. Keep alpha at 1 for an opaque jersey.")]
    [SerializeField] private Color color = Color.red;

    [Tooltip("Shader colour property. URP/Lit and HDRP use \"_BaseColor\". " +
             "Built-in / Standard uses \"_Color\". You are on URP, so leave this as _BaseColor.")]
    [SerializeField] private string colorProperty = "_BaseColor";

    private void Start()
    {
        ApplyColor(color);
    }

    /// <summary>
    /// Recolour this rider's jersey. Call this at runtime from a spawner, a
    /// settings menu, etc. (e.g. npc.GetComponentInChildren&lt;RiderColor&gt;().ApplyColor(c)).
    /// </summary>
    public void ApplyColor(Color newColor)
    {
        color = newColor;

        if (teamColorMaterial == null)
        {
            Debug.LogWarning(
                $"{name}: RiderColor has no teamColorMaterial assigned, so nothing was recoloured.");
            return;
        }

        MaterialPropertyBlock block = new MaterialPropertyBlock();

        // Pass 'true' so inactive children (e.g. a hidden mesh) are included.
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            // sharedMaterials reports the ORIGINAL asset in each slot, without cloning.
            Material[] slots = renderer.sharedMaterials;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != teamColorMaterial)
                {
                    continue;
                }

                // Override just this one submesh slot; the others stay as they were.
                renderer.GetPropertyBlock(block, i);
                block.SetColor(colorProperty, newColor);
                renderer.SetPropertyBlock(block, i);
            }
        }
    }
}