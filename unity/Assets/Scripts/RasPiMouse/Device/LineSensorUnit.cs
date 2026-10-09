using UnityEngine;

namespace RasPiMouse.Device
{
    public abstract class LineSensorUnit : MonoBehaviour
    {
        public float lightness;

        private void Update()
        {
            lightness = ColorToLight(GetColor());
        }

        protected abstract Color GetColor();

        private static float ColorToLight(Color c)
        {
            return 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        }
    }
}