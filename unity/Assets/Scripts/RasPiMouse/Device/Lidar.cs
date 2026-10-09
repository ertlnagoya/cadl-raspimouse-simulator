using UnityEngine;

namespace RasPiMouse.Device
{
    public class Lidar : MonoBehaviour
    {
        public float maxDistance = 3f;
        public Transform source;

        public bool detected;
        public float distance;
        public float rad;

        private void Update()
        {
            rad += Time.deltaTime;
            if (rad >= 2 * Mathf.PI) rad = 0f;

            source.localRotation = Quaternion.Euler(0, Mathf.Rad2Deg * rad, 0);
            var origin = source.position;
            var dir = source.forward;

            detected = Physics.Raycast(origin, dir, out var hit, maxDistance);
            distance = detected ? hit.distance : maxDistance;
            Debug.DrawRay(origin, distance * dir, detected ? Color.red : Color.green);
        }
    }
}