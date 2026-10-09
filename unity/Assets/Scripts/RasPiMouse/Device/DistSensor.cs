using System.Linq;
using Unity.Collections;
using UnityEngine;

namespace RasPiMouse.Device
{
    public class DistSensor : MonoBehaviour
    {
        private DistSensorUnit[] _units;

        private void Awake()
        {
            _units = GetComponentsInChildren<DistSensorUnit>();
            _units = _units.OrderBy(a => a.transform.localPosition.x).ToArray();
        }

        public float Distance(int index)
        {
            return _units[index].distance;
        }
    }
}