using UnityEngine;

namespace RasPiMouse
{
    public class ManualControl : MonoBehaviour
    {
        public bool b;
        private Mouse mouse;

        void Start()
        {
            mouse = GetComponent<Mouse>();
        }

        // Update is called once per frame
        void Update()
        {
            if (Input.GetKey(KeyCode.UpArrow))
            {
                mouse.Go(1);
            }
            else if (Input.GetKey(KeyCode.DownArrow))
            {
                mouse.Go(-1);
            }
            else if (Input.GetKey(KeyCode.RightArrow))
            {
                mouse.Turn(1);
            }
            else if (Input.GetKey(KeyCode.LeftArrow))
            {
                mouse.Turn(-1);
            }
            else
            {
                mouse.Stop();
            }
        }
    }
}