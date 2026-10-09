using UnityEngine;

namespace RasPiMouse.Device
{
    public class CameraLineSensorUnit : LineSensorUnit
    {
        private RenderTexture _tex;
        private Camera _camera;

        private const int Width = 64;

        private void Awake()
        {
            _tex = new RenderTexture(Width, Width, 0);
            _camera = GetComponentInChildren<Camera>();
            _camera.targetTexture = _tex;
            _camera.nearClipPlane = 0.0001f;
        }

        private void OnDestroy()
        {
            Destroy(_tex);
        }

        protected override Color GetColor()
        {
            var tex2D = new Texture2D(_tex.width, _tex.height, TextureFormat.ARGB32, false);
            RenderTexture.active = _tex;
            tex2D.ReadPixels(new Rect(0, 0, _tex.width, _tex.height), 0, 0);
            tex2D.Apply();

            return Average(tex2D);
        }

        private static Color Average(Texture2D tex)
        {
            var cols = tex.GetPixels();
            var count = 0;

            var avg = new Color(0, 0, 0);

            const int sqr = 20 * 20;
            var center = new Vector2(0.5f * Width, 0.5f * Width);
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Width; y++)
                {
                    if ((center - new Vector2(x, y)).sqrMagnitude < sqr)
                    {
                        avg += cols[x + y * Width];
                        count++;
                    }
                }
            }


            return avg / count;
        }
    }
}