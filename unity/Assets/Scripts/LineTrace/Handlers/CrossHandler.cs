using RasPiMouse;
using UnityEngine;

namespace LineTrace.Handlers
{
    public class CrossHandler
    {
        private readonly Mouse mouse;

        private Vector3 center;
        private Vector3 dest;
        private bool stage;

        private GoHandler goHandler;

        private const float CenterRadius = 0.25f;
        private const float DistEps1 = 0.029f;
        private const float DistEps2 = 0.06f;
        private const float RadEps = 0.14f;

        public CrossHandler(Mouse mouse)
        {
            goHandler = new GoHandler(mouse);
            this.mouse = mouse;
        }

        public void Reset(Vector3 center1, Vector3 dest1)
        {
            stage = true;
            center = center1;
            dest = dest1;
        }

        public bool Update(Transform transform)
        {
            var position = transform.position;
            var forward = transform.forward;

            if (stage)
            {
                var gap = center - position;

                if (gap.magnitude > CenterRadius)
                {
                    goHandler.Update();
                }
                else
                {
                    var sa = Rot(forward, gap);
                    if (sa > RadEps)
                    {
                        mouse.Turn(-1);
                    }
                    else if (sa < -RadEps)
                    {
                        mouse.Turn(1);
                    }
                    else
                    {
                        mouse.Go(1);
                    }

                    if (gap.magnitude < DistEps1)
                    {
                        stage = false;
                    }
                }
            }
            else
            {
                var gap = dest - position;
                var sa = Rot(forward, gap);
                if (sa > RadEps)
                {
                    mouse.Turn(-1);
                }
                else if (sa < -RadEps)
                {
                    mouse.Turn(1);
                }
                else
                {
                    mouse.Go(1);
                    if (gap.magnitude < DistEps2)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static float Rot(Vector3 current, Vector3 dir)
        {
            var myZ = current.z;
            var myX = current.x;
            var dsZ = dir.z;
            var dsX = dir.x;

            var rad = Mathf.Atan2(dsZ, dsX) - Mathf.Atan2(myZ, myX);
            if (rad > Mathf.PI)
            {
                rad -= 2 * Mathf.PI;
            }
            else if(rad < -Mathf.PI)
            {
                rad += 2 * Mathf.PI;
            }

            return rad;
        }
    }
}