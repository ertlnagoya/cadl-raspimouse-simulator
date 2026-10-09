using LineTrace;
using UnityEngine;

namespace Display
{
    public class GoalCursor_CSoS : MonoBehaviour
    {
        public Pilot_CSoS pilot;
        private int preNum = -1;

        void Start()
        {
        }

        void FixedUpdate(){
            if (pilot.goal < 0) return;
            if (preNum != pilot.goal)
            {
                preNum = pilot.goal;
                transform.position = transform.parent.Find($"{preNum}").position;
            }
        }
    }
}