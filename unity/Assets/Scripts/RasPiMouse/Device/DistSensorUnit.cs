using UnityEngine;

namespace RasPiMouse.Device
{
    public class DistSensorUnit : MonoBehaviour
    {
        public float maxDistance = 0.3f;
        public Transform piv;
        
        public bool detected;
        public float distance;

        private void Update()
        {
            var origin = piv.position;
            var dir = transform.forward;
            detected = Physics.Raycast(origin, dir, out var hit, maxDistance);
            distance = detected ? hit.distance : maxDistance;
            Debug.DrawRay(origin, distance * dir, detected ? Color.red : Color.green);
        }
    }
}