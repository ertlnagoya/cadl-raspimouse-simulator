using UnityEngine;

namespace RasPiMouse
{
    public class Wheel : MonoBehaviour
    {
        public float direction;
        public int flip = 1;

        private MouseSetting setting;
        private ArticulationBody joint;
        private Mouse mouse;

        private void Awake()
        {
            joint = GetComponent<ArticulationBody>();
            const int defDyanmicVal = 10;
            joint.jointFriction = defDyanmicVal;
            joint.angularDamping = defDyanmicVal;

            mouse = (Mouse) GetComponentInParent(typeof(Mouse));
            LoadSetting(mouse.setting);
        }

        private void LoadSetting(MouseSetting setting1)
        {
            setting = setting1;

            var drive = joint.xDrive;
            drive.forceLimit = setting1.forceLimit;
            drive.stiffness = setting1.stiffness;
            drive.damping = setting1.damping;
            joint.xDrive = drive;
        }

        private void FixedUpdate()
        {
            if (joint.jointType == ArticulationJointType.FixedJoint) return;

            if (!Equals(mouse.setting, setting))
            {
                LoadSetting(mouse.setting);
            }

            var currentDrive = joint.xDrive;


            var newTargetDelta = flip * direction * Time.fixedDeltaTime * setting.speed;

            if (joint.jointType == ArticulationJointType.RevoluteJoint)
            {
                if (joint.twistLock == ArticulationDofLock.LimitedMotion)
                {
                    if (newTargetDelta + currentDrive.target > currentDrive.upperLimit)
                    {
                        currentDrive.target = currentDrive.upperLimit;
                    }
                    else if (newTargetDelta + currentDrive.target < currentDrive.lowerLimit)
                    {
                        currentDrive.target = currentDrive.lowerLimit;
                    }
                    else
                    {
                        currentDrive.target += newTargetDelta;
                    }
                }
                else
                {
                    currentDrive.target += newTargetDelta;
                }
            }
            else if (joint.jointType == ArticulationJointType.PrismaticJoint)
            {
                if (joint.linearLockX == ArticulationDofLock.LimitedMotion)
                {
                    if (newTargetDelta + currentDrive.target > currentDrive.upperLimit)
                    {
                        currentDrive.target = currentDrive.upperLimit;
                    }
                    else if (newTargetDelta + currentDrive.target < currentDrive.lowerLimit)
                    {
                        currentDrive.target = currentDrive.lowerLimit;
                    }
                    else
                    {
                        currentDrive.target += newTargetDelta;
                    }
                }
                else
                {
                    currentDrive.target += newTargetDelta;
                }
            }

            joint.xDrive = currentDrive;
        }
    }
}