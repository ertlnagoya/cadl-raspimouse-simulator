using UnityEngine;

namespace RasPiMouse.Device
{
    public class TexLineSensorUnit : LineSensorUnit
    {
        protected override Color GetColor()
        {
            if (Physics.Raycast(transform.position
                , -transform.up, out var hit, 0.5f))
            {
                var mr = hit.transform.GetComponent<MeshRenderer>();
                if (mr)
                {
                    var tex = mr.material.mainTexture as Texture2D;
                    if (tex && tex.isReadable)
                    {
                        var uv = hit.textureCoord;
                        return tex.GetPixel(Mathf.FloorToInt(uv.x * tex.width), Mathf.FloorToInt(uv.y * tex.height));
                    }

                    return mr.material.color;
                }
            }

            return Color.black;
        }
    }
}