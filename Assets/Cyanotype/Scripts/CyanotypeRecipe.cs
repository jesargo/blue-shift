using UnityEngine;

[System.Serializable]
public class CyanotypeRecipe
{
    [Range(2f, 40f)] public float facGramsPer100mL = 20f;
    [Range(1f, 25f)] public float kfcGramsPer100mL = 8f;

    [Tooltip("Set when sensitizer is first brushed on. A fresh sheet unlocks it.")]
    public bool locked;

    public void Set(float fac, float kfc)
    {
        if (locked) return;
        facGramsPer100mL = Mathf.Clamp(fac, 2f, 40f);
        kfcGramsPer100mL = Mathf.Clamp(kfc, 1f, 25f);
    }

    public void Unlock()
    {
        locked = false;
    }
}