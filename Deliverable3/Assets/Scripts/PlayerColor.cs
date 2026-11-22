using UnityEngine;

public class PlayerColor : MonoBehaviour
{
    static readonly int HueId = Shader.PropertyToID("_Hue");

    SpriteRenderer m_sr;
    MaterialPropertyBlock m_block;

    void Awake()
    {
        m_sr = GetComponentInChildren<SpriteRenderer>();
        m_block = new MaterialPropertyBlock();
    }

    // Same index means that all PCs see the same color
    public void SetIndex(int index)
    {
        SetHue((index * 0.618034f) % 1f);
    }

    public void SetHue(float hue)
    {
        m_sr.GetPropertyBlock(m_block);
        m_block.SetFloat(HueId, hue);
        m_sr.SetPropertyBlock(m_block);
    }
}