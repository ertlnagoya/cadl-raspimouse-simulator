using System;

namespace RasPiMouse
{
    [Serializable]
    public struct MouseSetting
    {
        public float speed;
        public float stiffness;
        public float damping;
        public float forceLimit;
    }
}