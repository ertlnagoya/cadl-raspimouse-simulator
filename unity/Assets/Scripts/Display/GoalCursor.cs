using LineTrace;
using UnityEngine;

namespace Display
{
    public class GoalCursor : MonoBehaviour
    {
        public Pilot pilot;
        private int preNum = -1;

        void Start()
        {
        }

        void Update()
        {
            if (!pilot) return;
            if (pilot.goal < 0) return;
            if (preNum != pilot.goal)
            {
                preNum = pilot.goal;
                transform.position = transform.parent.Find($"{preNum}").position;
            }
        }
    }
}