using System.Linq;
using UnityEngine;

namespace RasPiMouse.Device
{
    public class LineSensor : MonoBehaviour
    {
        private LineSensorUnit[] _units;

        private void Awake()
        {
            _units = GetComponentsInChildren<LineSensorUnit>();
            _units = _units.OrderBy(a => a.transform.localPosition.x).ToArray();
        }

        public float Lightness(int index)
        {
            return _units[index].lightness;
        }

        public float MaxLightness(int[] indexs)
        {
            var lightness = 0f;
            foreach (var index in indexs)
            {
                var cur = _units[index].lightness;
                if (cur > lightness) lightness = cur;
            }

            return lightness;
        }

        public float MinLightness(int[] indexs)
        {
            var lightness = 1f;
            foreach (var index in indexs)
            {
                var cur = _units[index].lightness;
                if (cur < lightness) lightness = cur;
            }

            return lightness;
        }
    }
}