using RasPiMouse.Device;
using UnityEngine;

namespace RasPiMouse
{
    public class Mouse : MonoBehaviour
    {
        public MouseSetting setting;

        public Wheel rightWheel;
        public Wheel leftWheel;

        [HideInInspector] public LineSensor lineSensor;
        [HideInInspector] public DistSensor distSensor;
        [HideInInspector] public Lidar lidar;
        [HideInInspector] public MiniCamera miniCamera;

        private void Awake()
        {
            lineSensor = GetComponentInChildren<LineSensor>();
            distSensor = GetComponentInChildren<DistSensor>();
            lidar = GetComponentInChildren<Lidar>();
            miniCamera = GetComponentInChildren<MiniCamera>();
        }

        public void Turn(float direction)
        {
            rightWheel.direction = -direction;
            leftWheel.direction = direction;
        }

        public void Go(float direction)
        {
            rightWheel.direction = 0.75f * direction;
            leftWheel.direction = 0.75f * direction;
        }

        public void Stop()
        {
            rightWheel.direction = 0;
            leftWheel.direction = 0;
        }
    }
}